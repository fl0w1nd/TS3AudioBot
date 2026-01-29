// TS3AudioBot - An advanced Musicbot for Teamspeak 3
// Copyright (C) 2017  TS3AudioBot contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.AspNetCore.Http;
using TS3AudioBot;
using TS3AudioBot.Config;
using TS3AudioBot.Helper;
using TS3AudioBot.Web.Api;
using TSLib;
using TSLib.Audio;
using TSLib.Full;
using TSLib.Helper;
using TSLib.Messages;
using TSLib.Scheduler;

namespace TS3AudioBot.Audio
{
	public sealed class RecordingManager : IDisposable
	{
		private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();
		private static readonly TimeSpan MixInterval = TimeSpan.FromMilliseconds(20);
		private static readonly TimeSpan SenderTimeout = TimeSpan.FromSeconds(30);
		private static readonly TimeSpan MaxSegmentDuration = TimeSpan.FromHours(1);
		private static readonly TimeSpan WaveformFlushInterval = TimeSpan.FromSeconds(1);
		private const string RecordingTableName = "recordings";
		private const int RecordingSchemaVersion = 2;
		private const int WaveformSampleRate = 50; // 20ms ticks
		private const int WaveformHeaderSize = 16; // "TSWF" + version/flags/reserved + sampleRate + sampleCount
		private static readonly Uid MixedWaveformUid = new Uid("mixed");
		private const string MixedWaveformName = "Mixed";

		private readonly ConfBot config;
		private readonly Ts3Client ts3Client;
		private readonly TsFullClient ts3FullClient;
		private readonly DedicatedTaskScheduler scheduler;
		private readonly DbStore database;
		private readonly LiteCollection<RecordingEntry> recordingTable;
		private readonly int botId;
		private TickWorker? mixTicker;
		private TickWorker? stopDelayWorker;
		private readonly Dictionary<ClientId, PcmBuffer> senderBuffers = new Dictionary<ClientId, PcmBuffer>();
		private readonly Dictionary<Uid, string> participants = new Dictionary<Uid, string>();
		private readonly object recordLock = new object();
		private readonly RecordingSink sink;

		private EncoderPipe? encoder;
		private OggOpusWriter? writer;
		private global::System.IO.FileInfo? currentFile;
		private string? currentEntryId;
		private DateTime startTime;
		private string? pendingStopReason;
		private DateTime lastAloneCheck;
		private DateTime lastFlush;
		private DateTime lastDbUpdate;
		private DateTime lastWaveformFlush;
		private TimeSpan? currentDurationOverride;
		private bool isRecording;
		private bool isDisposed;
		private byte[]? mixBuffer;
		private byte[]? tmpBuffer;
		private int[]? accBuffer;
		private int waveformSampleIndex;
		private WaveformSet? waveformSet;

		private AsyncEventHandler<AloneChanged>? onAloneChanged;
		private AsyncEventHandler? onBotConnected;
		private AsyncEventHandler<DisconnectEventArgs>? onBotDisconnected;
		private NotifyEventHandler<ClientEnterView>? onClientEnterView;
		private NotifyEventHandler<ClientMoved>? onClientMoved;
		private NotifyEventHandler<ClientLeftView>? onClientLeftView;
		private NotifyEventHandler<ClientUpdated>? onClientUpdated;
		private EventHandler<ConfigChangedEventArgs<bool>>? onEnabledChanged;
		private EventHandler<ConfigChangedEventArgs<TimeSpan>>? onStopDelayChanged;

		public bool Enabled => config.Recording.Enabled.Value;
		public bool Active => isRecording;
		public RecordingInfo? Current
		{
			get
			{
				if (!isRecording)
					return null;
				lock (recordLock)
				{
					writer?.Flush();
					if (currentFile is null)
						return null;
					MaybeUpdateCurrentEntry(DateTime.UtcNow);
					return BuildRecordingInfo(currentFile, startTime, null, isOpen: true, currentDurationOverride, GetParticipantsSnapshot(), GetWaveformsSnapshot());
				}
			}
		}


		public RecordingManager(ConfBot config, Ts3Client ts3Client, TsFullClient ts3FullClient, DedicatedTaskScheduler scheduler, DbStore database, Id botId)
		{
			this.config = config;
			this.ts3Client = ts3Client;
			this.ts3FullClient = ts3FullClient;
			this.scheduler = scheduler;
			this.database = database;
			this.botId = botId.Value;
			recordingTable = database.GetCollection<RecordingEntry>(RecordingTableName);
			EnsureRecordingSchema();

			sink = new RecordingSink(this);
			AttachAudioPipeline();

			onAloneChanged = async (_, e) =>
			{
				if (e.Alone)
					ScheduleStop();
				else
					CancelStopAndResume();
				await System.Threading.Tasks.Task.CompletedTask;
			};
			onBotConnected = async (_, __) =>
			{
				RecheckPartyState();
				await System.Threading.Tasks.Task.CompletedTask;
			};
			onBotDisconnected = async (_, __) =>
			{
				StopRecording();
				await System.Threading.Tasks.Task.CompletedTask;
			};
			onClientEnterView = (_, __) => OnParticipantsChanged();
			onClientMoved = (_, __) => OnParticipantsChanged();
			onClientLeftView = (_, __) => OnParticipantsChanged();
			onClientUpdated = (_, __) => OnParticipantsChanged();
			onEnabledChanged = (s, e) =>
			{
				if (!e.NewValue)
					StopRecording();
				else
					StartRecordingIfAllowed();
			};

			ts3Client.OnAloneChanged += onAloneChanged;
			ts3Client.OnBotConnected += onBotConnected;
			ts3Client.OnBotDisconnected += onBotDisconnected;

			ts3FullClient.OnClientEnterView += onClientEnterView;
			ts3FullClient.OnClientMoved += onClientMoved;
			ts3FullClient.OnClientLeftView += onClientLeftView;
			ts3FullClient.OnClientUpdated += onClientUpdated;

			config.Recording.Enabled.Changed += onEnabledChanged;
		}

		public async Task InitializeAsync()
		{
			mixTicker = await scheduler.Invoke(() => scheduler.CreateTimer(MixTick, MixInterval, false));
			stopDelayWorker = await scheduler.Invoke(() => scheduler.CreateTimer(StopRecording, config.Recording.StopDelay.Value, false));
			
			onStopDelayChanged = (s, e) =>
			{
				if (stopDelayWorker != null)
					stopDelayWorker.Interval = e.NewValue;
			};
			config.Recording.StopDelay.Changed += onStopDelayChanged;
			
			// Auto-fix orphaned recordings from crash/kill
			_ = System.Threading.Tasks.Task.Run(RecoverOrphans);
		}

		private void EnsureRecordingSchema()
		{
			var meta = database.GetMetaData(RecordingTableName);
			recordingTable.EnsureIndex(x => x.BotId);
			recordingTable.EnsureIndex(x => x.StartUtc);
			recordingTable.EnsureIndex(x => x.IsOpen);
			recordingTable.EnsureIndex(x => x.FileId, unique: true);
			// participants are stored in the array field; we filter in-memory for readability

			if (meta.Version < RecordingSchemaVersion)
			{
				meta.Version = RecordingSchemaVersion;
				database.UpdateMetaData(meta);
			}
		}

		public void Dispose()
		{
			if (isDisposed)
				return;
			isDisposed = true;
			StopRecording();
			mixTicker?.Disable();
			stopDelayWorker?.Disable();

			if (onAloneChanged != null) ts3Client.OnAloneChanged -= onAloneChanged;
			if (onBotConnected != null) ts3Client.OnBotConnected -= onBotConnected;
			if (onBotDisconnected != null) ts3Client.OnBotDisconnected -= onBotDisconnected;
			if (onClientEnterView != null) ts3FullClient.OnClientEnterView -= onClientEnterView;
			if (onClientMoved != null) ts3FullClient.OnClientMoved -= onClientMoved;
			if (onClientLeftView != null) ts3FullClient.OnClientLeftView -= onClientLeftView;
			if (onClientUpdated != null) ts3FullClient.OnClientUpdated -= onClientUpdated;
			if (onEnabledChanged != null) config.Recording.Enabled.Changed -= onEnabledChanged;
			if (onStopDelayChanged != null) config.Recording.StopDelay.Changed -= onStopDelayChanged;
		}

		public void SetEnabled(bool enabled)
		{
			config.Recording.Enabled.Value = enabled;
			config.SaveWhenExists().UnwrapToLog(Log);
		}

		public IReadOnlyList<RecordingInfo> ListRecordings(DateTime? from = null, DateTime? to = null, string? uid = null, string? name = null)
		{
			uid = NormalizeFilter(uid);
			name = NormalizeFilter(name);

			var fromUtc = from.HasValue ? (DateTime?)ToUtc(from.Value) : null;
			var toValue = to;
			if (toValue.HasValue && toValue.Value.TimeOfDay == TimeSpan.Zero)
				toValue = toValue.Value.Date.AddDays(1).AddTicks(-1);
			var toUtc = toValue.HasValue ? (DateTime?)ToUtc(toValue.Value) : null;

			var query = Query.All(nameof(RecordingEntry.StartUtc), Query.Descending);
			query = Query.And(query, Query.EQ(nameof(RecordingEntry.BotId), botId));
			if (fromUtc.HasValue)
				query = Query.And(query, Query.GTE(nameof(RecordingEntry.StartUtc), fromUtc.Value));
			if (toUtc.HasValue)
				query = Query.And(query, Query.LTE(nameof(RecordingEntry.StartUtc), toUtc.Value));
			var uidTokens = ParseFilterTokens(uid);
			var nameTokens = ParseFilterTokens(name);

			var entries = recordingTable.Find(query).ToList();
			var current = Current;
			var results = new List<RecordingInfo>(entries.Count);

			foreach (var entry in entries)
			{
				if (!MatchesParticipantFilter(entry.Participants, nameTokens, uidTokens))
					continue;
				if (currentEntryId != null && current != null && entry.Id == currentEntryId)
				{
					results.Add(current);
					continue;
				}
				results.Add(BuildRecordingInfo(entry));
			}

			return results;
		}

		public IReadOnlyList<RecordingParticipant> ListParticipants(DateTime? from = null, DateTime? to = null)
		{
			var fromUtc = from.HasValue ? (DateTime?)ToUtc(from.Value) : null;
			var toValue = to;
			if (toValue.HasValue && toValue.Value.TimeOfDay == TimeSpan.Zero)
				toValue = toValue.Value.Date.AddDays(1).AddTicks(-1);
			var toUtc = toValue.HasValue ? (DateTime?)ToUtc(toValue.Value) : null;

			var query = Query.All(nameof(RecordingEntry.StartUtc), Query.Descending);
			query = Query.And(query, Query.EQ(nameof(RecordingEntry.BotId), botId));
			if (fromUtc.HasValue)
				query = Query.And(query, Query.GTE(nameof(RecordingEntry.StartUtc), fromUtc.Value));
			if (toUtc.HasValue)
				query = Query.And(query, Query.LTE(nameof(RecordingEntry.StartUtc), toUtc.Value));

			var entries = recordingTable.Find(query).ToList();
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var entry in entries)
			{
				if (entry.Participants is null)
					continue;
				foreach (var participant in entry.Participants)
				{
					if (string.IsNullOrWhiteSpace(participant.Uid))
						continue;
					if (!map.TryGetValue(participant.Uid, out var existing) || string.IsNullOrWhiteSpace(existing))
						map[participant.Uid] = participant.Name;
				}
			}

			return map
				.Select(p => new RecordingParticipant { Uid = p.Key, Name = p.Value })
				.OrderBy(p => p.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
				.ThenBy(p => p.Uid, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}

		public bool DeleteRecording(string id)
		{
			var entry = FindEntryByFileId(id);
			if (entry is null)
				return false;
			if (currentEntryId != null && entry.Id == currentEntryId)
				return false;

			var file = ResolveRecordingFile(id);
			if (file != null && file.Exists)
			{
				file.Delete();
				CleanupEmptyDirs(file.Directory);
			}

			if (entry.Waveforms != null && entry.Waveforms.Count > 0)
			{
				foreach (var wf in entry.Waveforms)
				{
					var wfFile = ResolveRecordingFile(wf.FileId);
					if (wfFile != null && wfFile.Exists)
					{
						try { wfFile.Delete(); } catch { }
						CleanupEmptyDirs(wfFile.Directory);
					}
				}
			}
			else if (file != null)
			{
				DeleteWaveformFiles(file, null);
			}

			recordingTable.Delete(entry.Id);
			return true;
		}

		public DataStream OpenRecordingStream(string id, bool follow = false)
		{
			var file = ResolveRecordingFile(id) ?? throw Error.LocalStr("Recording not found.");
			return new DataStream(async response =>
			{
				try
				{
					response.ContentType = "audio/ogg";
					response.Headers["Accept-Ranges"] = "bytes";
					response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
					response.Headers["Pragma"] = "no-cache";
					response.Headers["X-Accel-Buffering"] = "no";
					await using var stream = file.Open(System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
					if (follow && stream.CanSeek)
					{
						if (TryWriteOggHeaders(stream, response))
						{
							stream.Seek(stream.Length, SeekOrigin.Begin);
						}
						else
						{
							stream.Seek(0, SeekOrigin.Begin);
						}
					}

					if (!follow)
					{
						file.Refresh();
						long totalLength = file.Length;
						var request = response.HttpContext.Request;
						if (request.Headers.TryGetValue("Range", out var rangeHeader) && TryParseRange(rangeHeader.ToString(), totalLength, out var rangeStart, out var rangeEnd))
						{
							response.StatusCode = StatusCodes.Status206PartialContent;
							response.Headers["Content-Range"] = $"bytes {rangeStart}-{rangeEnd}/{totalLength}";
							response.ContentLength = rangeEnd - rangeStart + 1;
							stream.Seek(rangeStart, SeekOrigin.Begin);
							await CopyRangeAsync(stream, response, response.ContentLength.Value, response.HttpContext.RequestAborted);
							return;
						}
						else if (request.Headers.ContainsKey("Range"))
						{
							response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
							response.Headers["Content-Range"] = $"bytes */{totalLength}";
							return;
						}

						response.ContentLength = totalLength;
						await stream.CopyToAsync(response.Body, response.HttpContext.RequestAborted);
						return;
					}

					var buffer = new byte[64 * 1024];
					while (true)
					{
						int read = await stream.ReadAsync(buffer, 0, buffer.Length, response.HttpContext.RequestAborted);
						if (read > 0)
						{
							await response.Body.WriteAsync(buffer.AsMemory(0, read), response.HttpContext.RequestAborted);
							await response.Body.FlushAsync(response.HttpContext.RequestAborted);
							continue;
						}

						bool stillRecording;
						lock (recordLock)
						{
							stillRecording = isRecording && currentFile != null && file.FullName == currentFile.FullName;
						}
						if (!stillRecording)
							break;

						await System.Threading.Tasks.Task.Delay(250, response.HttpContext.RequestAborted);
					}
				}
				catch (OperationCanceledException)
				{
				}
				catch (IOException)
				{
				}
			});
		}

		public DataStream OpenWaveformStream(string id, string uid)
		{
			var lookupUid = string.IsNullOrWhiteSpace(uid) ? MixedWaveformUid.Value : uid;
			var file = ResolveWaveformFile(id, lookupUid) ?? throw Error.LocalStr("Waveform not found.");
			return new DataStream(async response =>
			{
				try
				{
					response.ContentType = "application/octet-stream";
					response.Headers["Accept-Ranges"] = "bytes";
					response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
					response.Headers["Pragma"] = "no-cache";
					response.Headers["X-Accel-Buffering"] = "no";
					await using var stream = file.Open(System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);

					file.Refresh();
					long totalLength = file.Length;
					var request = response.HttpContext.Request;
					if (request.Headers.TryGetValue("Range", out var rangeHeader) && TryParseRange(rangeHeader.ToString(), totalLength, out var rangeStart, out var rangeEnd))
					{
						response.StatusCode = StatusCodes.Status206PartialContent;
						response.Headers["Content-Range"] = $"bytes {rangeStart}-{rangeEnd}/{totalLength}";
						response.ContentLength = rangeEnd - rangeStart + 1;
						stream.Seek(rangeStart, SeekOrigin.Begin);
						await CopyRangeAsync(stream, response, response.ContentLength.Value, response.HttpContext.RequestAborted);
						return;
					}
					else if (request.Headers.ContainsKey("Range"))
					{
						response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
						response.Headers["Content-Range"] = $"bytes */{totalLength}";
						return;
					}

					response.ContentLength = totalLength;
					await stream.CopyToAsync(response.Body, response.HttpContext.RequestAborted);
				}
				catch (OperationCanceledException)
				{
				}
				catch (IOException)
				{
				}
			});
		}

		private static bool TryParseRange(string rangeHeader, long totalLength, out long start, out long end)
		{
			start = 0;
			end = 0;
			if (string.IsNullOrWhiteSpace(rangeHeader) || !rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
				return false;
			var range = rangeHeader.Substring("bytes=".Length).Trim();
			var parts = range.Split('-', 2);
			if (parts.Length != 2)
				return false;
			
			if (string.IsNullOrEmpty(parts[0]))
			{
				// Suffix range: bytes=-N (last N bytes)
				if (!long.TryParse(parts[1], out var lastN) || lastN <= 0)
					return false;
				
				end = totalLength - 1;
				if (lastN >= totalLength)
					start = 0;
				else
					start = totalLength - lastN;
				
				return true;
			}

			if (!long.TryParse(parts[0], out start) || start < 0)
				return false;

			if (string.IsNullOrWhiteSpace(parts[1]))
			{
				end = totalLength - 1;
			}
			else
			{
				if (!long.TryParse(parts[1], out end) || end < start)
					return false;
			}

			if (start >= totalLength)
				return false;
			
			if (end >= totalLength)
				end = totalLength - 1;

			return true;
		}

		private static async System.Threading.Tasks.Task CopyRangeAsync(Stream stream, HttpResponse response, long length, System.Threading.CancellationToken ct)
		{
			var buffer = new byte[64 * 1024];
			long remaining = length;
			while (remaining > 0)
			{
				int read = await stream.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, remaining), ct);
				if (read <= 0)
					break;
				await response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
				remaining -= read;
			}
		}

		private static bool TryWriteOggHeaders(Stream stream, HttpResponse response)
		{
			try
			{
				stream.Seek(0, SeekOrigin.Begin);
				for (int i = 0; i < 2; i++)
				{
					if (!TryReadOggPage(stream, out var page))
						return false;
					response.Body.Write(page, 0, page.Length);
				}
				response.Body.Flush();
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static bool TryReadOggPage(Stream stream, out byte[] page)
		{
			page = Array.Empty<byte>();
			var header = new byte[27];
			if (stream.Read(header, 0, header.Length) != header.Length)
				return false;
			if (header[0] != (byte)'O' || header[1] != (byte)'g' || header[2] != (byte)'g' || header[3] != (byte)'S')
				return false;

			int segments = header[26];
			var segTable = new byte[segments];
			if (segments > 0 && stream.Read(segTable, 0, segments) != segments)
				return false;

			int payloadLen = 0;
			for (int i = 0; i < segments; i++)
				payloadLen += segTable[i];
			var payload = new byte[payloadLen];
			if (payloadLen > 0 && stream.Read(payload, 0, payloadLen) != payloadLen)
				return false;

			page = new byte[header.Length + segTable.Length + payload.Length];
			Buffer.BlockCopy(header, 0, page, 0, header.Length);
			if (segTable.Length > 0)
				Buffer.BlockCopy(segTable, 0, page, header.Length, segTable.Length);
			if (payload.Length > 0)
				Buffer.BlockCopy(payload, 0, page, header.Length + segTable.Length, payload.Length);
			return true;
		}

		private void AttachAudioPipeline()
		{
			var packetReader = new AudioPacketReader();
			var decoder = new DecoderPipe();
			packetReader.Chain(decoder).Chain(sink);
			ts3FullClient.Chain(packetReader);
		}

		private void StartRecordingIfAllowed()
		{
			if (!Enabled || isRecording)
				return;

			if (IsAlone())
				return;

			stopDelayWorker?.Disable();
			StartRecording();
		}

		private void StartRecording()
		{
			if (isRecording || !Enabled)
				return;

			global::System.IO.FileInfo? newFile = null;
			System.IO.Stream? newStream = null;
			OggOpusWriter? newWriter = null;
			EncoderPipe? newEncoder = null;
			string? newEntryId = null;
			DateTime newStartTime = TrimToSecond(UtcNow());

			try
			{
				newFile = CreateNewRecordingFile(newStartTime);
				newStream = newFile.Open(System.IO.FileMode.Create, global::System.IO.FileAccess.Write, global::System.IO.FileShare.Read);
				newWriter = new OggOpusWriter(newStream);

				participants.Clear();
				RefreshParticipantsSnapshot();
				var participantsSnapshot = GetParticipantsSnapshot();
				newEntryId = CreateRecordingEntry(newFile, newStartTime, participantsSnapshot);

				newEncoder = new EncoderPipe(Codec.OpusMusic)
				{
					Bitrate = Math.Max(1, config.Recording.Bitrate.Value) * 1000
				};
				newEncoder.OutStream = newWriter;

				lock (recordLock)
				{
					if (isRecording || !Enabled)
					{
						// Already started or disabled during preparation
						throw new OperationCanceledException("Recording already started or disabled during preparation");
					}

					startTime = newStartTime;
					lastFlush = UtcNow();
					lastDbUpdate = UtcNow();
					currentFile = newFile;
					writer = newWriter;
					encoder = newEncoder;
					currentEntryId = newEntryId;

					mixBuffer = new byte[encoder.PacketSize];
					tmpBuffer = new byte[encoder.PacketSize];
					accBuffer = new int[encoder.PacketSize / 2];
					waveformSampleIndex = 0;
					waveformSet = new WaveformSet(currentFile, WaveformSampleRate);
					waveformSet.Tracks[MixedWaveformUid] = CreateWaveformTrack(MixedWaveformUid, MixedWaveformName, currentFile, waveformSampleIndex);
					lastWaveformFlush = UtcNow();
					
					isRecording = true;
					mixTicker?.Enable();

					// Prevent cleanup in finally
					newStream = null;
					newWriter = null;
					newEncoder = null;
					newFile = null;
				}

				Log.Info("Recording started: {0}", currentFile!.FullName);
			}
			catch (Exception ex)
			{
				if (!(ex is OperationCanceledException))
					Log.Error(ex, "Failed to start recording");

				newEncoder?.Dispose();
				newWriter?.Dispose();
				newStream?.Dispose();

				if (newFile != null)
				{
					try { if (newFile.Exists) newFile.Delete(); } catch { }
				}

				if (newEntryId != null)
				{
					try { recordingTable.Delete(newEntryId); } catch { }
				}
			}
		}

		private void StopRecording()
		{
			global::System.IO.FileInfo? fileToFinalize;
			DateTime stopTime;
			string? reason;
			TimeSpan? durationOverride;
			string? stoppedEntryId;
			WaveformSet? waveformsToFinalize;

			lock (recordLock)
			{
				if (!isRecording)
					return;

				durationOverride = writer?.Duration ?? currentDurationOverride;
				isRecording = false;
				mixTicker?.Disable();
				encoder?.Dispose();
				writer?.Dispose();
				encoder = null;
				writer = null;
				// Capture locals for finalization
				fileToFinalize = currentFile;
				mixBuffer = null;
				tmpBuffer = null;
				accBuffer = null;
				currentDurationOverride = null;
				reason = pendingStopReason;
				pendingStopReason = null;
				stoppedEntryId = currentEntryId;
				currentEntryId = null; 
				waveformsToFinalize = waveformSet;
				waveformSet = null;
				waveformSampleIndex = 0;
				
				stopTime = TrimToSecond(UtcNow());

				senderBuffers.Clear();
			}

			// Run I/O outside lock
			if (fileToFinalize != null)
				FinalizeCurrentFile(fileToFinalize, stopTime, reason, durationOverride, stoppedEntryId, waveformsToFinalize);
			EnforceMaxSize();
		}

		private void ScheduleStop()
		{
			if (!isRecording)
				return;
			pendingStopReason = $"channel empty > {config.Recording.StopDelay.Value}";
			if (stopDelayWorker != null)
			{
				stopDelayWorker.Interval = config.Recording.StopDelay.Value;
				stopDelayWorker.Enable();
			}
		}

		private void CancelStopAndResume()
		{
			pendingStopReason = null;
			stopDelayWorker?.Disable();
			StartRecordingIfAllowed();
		}

		private void MixTick()
		{
			if (!isRecording || encoder is null || mixBuffer is null || tmpBuffer is null || accBuffer is null)
				return;

			var frameSize = encoder.PacketSize;
			Array.Clear(accBuffer, 0, accBuffer.Length);
			int contributors = 0;
			Dictionary<Uid, WaveformSample>? rmsSamples = null;

			// Check rotation condition outside the main mix lock if possible, or inside?
			// To avoid blocking the mix lock with IO, we check condition then perform rotation separately.
			// However, since RotateRecording needs to coordinate with the lock, we can call it.
			// But the instruction says "RotateRecordingUnsafe is doing heavy file I/O ... refactor so ... happens outside the lock".
			// So we check first.
			bool needRotation = false;
			DateTime now = UtcNow();
			lock (recordLock)
			{
				if (isRecording && now - startTime >= MaxSegmentDuration)
					needRotation = true;
			}
			
			if (needRotation)
				RotateRecording(now);

			lock (recordLock)
			{
				if (!isRecording || encoder is null || mixBuffer is null || tmpBuffer is null || accBuffer is null)
					return;

				// Re-read shared state after potential rotation
				now = UtcNow();

				MaybeScheduleStopIfAlone(now);
				if (waveformSet != null)
					rmsSamples = new Dictionary<Uid, WaveformSample>();
				var remove = new List<ClientId>();
				foreach (var kvp in senderBuffers)
				{
					if (now - kvp.Value.ReadLastWrite() > SenderTimeout)
					{
						remove.Add(kvp.Key);
						continue;
					}

					bool hasData = kvp.Value.ReadFrame(tmpBuffer, frameSize);
					if (!hasData)
						continue;

					var pcm = MemoryMarshal.Cast<byte, short>(tmpBuffer.AsSpan(0, frameSize));
					double sumSq = 0;
					for (int i = 0; i < pcm.Length; i++)
					{
						accBuffer[i] += pcm[i];
						var sample = pcm[i];
						sumSq += (double)sample * sample;
					}
					contributors++;

					if (rmsSamples != null && TryGetClientIdentity(kvp.Key, out var uid, out var name))
					{
						var rms = Math.Sqrt(sumSq / pcm.Length) / short.MaxValue;
						rmsSamples[uid] = new WaveformSample(NormalizeRmsToByte(rms), name, rms);
					}
				}

				foreach (var id in remove)
					senderBuffers.Remove(id);

				if (contributors == 0 && mixBuffer != null)
				{
					Array.Clear(mixBuffer, 0, mixBuffer.Length);
				}
				else if (mixBuffer != null)
				{
					var outPcm = MemoryMarshal.Cast<byte, short>(mixBuffer.AsSpan(0, frameSize));
					for (int i = 0; i < outPcm.Length; i++)
						outPcm[i] = (short)Math.Max(Math.Min(accBuffer[i], short.MaxValue), short.MinValue);
				}
				
				if (writer != null)
					currentDurationOverride = writer.Duration;

				if (encoder != null && mixBuffer != null)
				{
					encoder.Write(mixBuffer, null);
					MaybeFlushWriter(UtcNow(), writer);
				}

				if (rmsSamples != null)
				{
					double sumRmsSq = 0;
					foreach (var sample in rmsSamples.Values)
						sumRmsSq += sample.Rms * sample.Rms;
					var mixedRms = Math.Sqrt(sumRmsSq);
					if (mixedRms > 1) mixedRms = 1;
					rmsSamples[MixedWaveformUid] = new WaveformSample(NormalizeRmsToByte(mixedRms), MixedWaveformName, mixedRms);
				}

				if (waveformSet != null)
					AppendWaveformSamplesLocked(rmsSamples, now);
			}
		}

		private void PushPcm(ClientId sender, ReadOnlySpan<byte> data)
		{
			if (sender == ClientId.Null || data.IsEmpty)
				return;

			var self = ts3FullClient.Book.Self();
			if (self == null)
				return;

			// Ensure the sender is in the same channel as the bot
			if (!ts3FullClient.Book.Clients.TryGetValue(sender, out var client) || client.Channel != self.Channel)
				return;

			lock (recordLock)
			{
				if (!isRecording)
					return;
				if (!senderBuffers.TryGetValue(sender, out var buffer))
				{
					buffer = new PcmBuffer();
					senderBuffers[sender] = buffer;
				}
				buffer.Write(data);
			}
		}

		private void RecheckPartyState()
		{
			if (!Enabled)
				return;
			if (IsAlone())
			{
				if (isRecording)
					ScheduleStop();
			}
			else
			{
				CancelStopAndResume();
			}
		}

		private void OnParticipantsChanged()
		{
			if (isRecording)
			{
				lock (recordLock)
				{
				RefreshParticipantsSnapshot();
				UpdateCurrentEntryParticipantsLocked(UtcNow());
				}
			}
			RecheckPartyState();
		}

		private void MaybeScheduleStopIfAlone(DateTime now)
		{
			if (!isRecording)
				return;
			if (now - lastAloneCheck < TimeSpan.FromSeconds(1))
				return;
			lastAloneCheck = now;
			if (IsAlone())
				ScheduleStop();
			else
				CancelStopAndResume();
		}

		private void MaybeFlushWriter(DateTime utcNow, OggOpusWriter? localWriter)
		{
			if (localWriter is null)
				return;
			var interval = TimeSpan.FromSeconds(1);
			if (utcNow - lastFlush < interval)
				return;
			lastFlush = utcNow;
			localWriter.Flush();
			MaybeUpdateCurrentEntry(utcNow);
		}

		private bool IsAlone()
		{
			var self = ts3FullClient.Book.Self();
			if (self is null || !self.Uid.HasValue)
				return true;
			var ownChannel = self.Channel;
			var selfUid = self.Uid.Value;
			var excludeUids = config.Recording.ExcludeUids.Value;
			var excludeSet = new HashSet<string>(excludeUids, StringComparer.Ordinal);
			var hasValidParticipant = ts3FullClient.Book.Clients.Values.Any(c =>
				c.Channel == ownChannel &&
				c != self &&
				c.Uid != selfUid &&
				(c.Uid is null || !excludeSet.Contains(c.Uid.Value.Value)));
			return !hasValidParticipant;
		}

		private RecordingEntry? FindEntryByFileId(string fileId)
		{
			if (string.IsNullOrWhiteSpace(fileId))
				return null;
			return recordingTable.FindOne(x => x.BotId == botId && x.FileId == fileId);
		}

		private void DeleteEntryByFile(global::System.IO.FileInfo file)
		{
			var fileId = BuildFileId(file);
			var entry = recordingTable.FindOne(x => x.BotId == botId && x.FileId == fileId);
			if (entry == null)
			{
				DeleteWaveformFiles(file, null);
				return;
			}

			if (entry.Waveforms != null && entry.Waveforms.Count > 0)
			{
				foreach (var wf in entry.Waveforms)
				{
					var wfFile = ResolveRecordingFile(wf.FileId);
					if (wfFile != null && wfFile.Exists)
					{
						try { wfFile.Delete(); } catch { }
						CleanupEmptyDirs(wfFile.Directory);
					}
				}
			}
			recordingTable.Delete(entry.Id);
		}

		private string BuildFileId(global::System.IO.FileInfo file)
		{
			var baseDir = GetRecordingBaseDir();
			var fullPath = Path.GetFullPath(file.FullName);
			var basePath = baseDir.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
			{
				var relative = fullPath.Substring(basePath.Length);
				return relative.Replace(Path.DirectorySeparatorChar, '/');
			}
			return file.Name;
		}

		private string CreateRecordingEntry(global::System.IO.FileInfo file, DateTime startUtc, List<RecordingParticipant> participantsList)
		{
			startUtc = EnsureUtc(startUtc);
			var nowUtc = UtcNow();
			var entry = new RecordingEntry
			{
				Id = Guid.NewGuid().ToString("N"),
				BotId = botId,
				FileId = BuildFileId(file),
				FileName = file.Name,
				StartUtc = startUtc,
				EndUtc = null,
				SizeBytes = file.Exists ? file.Length : 0,
				DurationMs = null,
				IsOpen = true,
				Participants = participantsList,
				CreatedUtc = nowUtc,
				UpdatedUtc = nowUtc
			};
			recordingTable.Insert(entry);
			return entry.Id;
		}

		private void MaybeUpdateCurrentEntry(DateTime utcNow)
		{
			lock (recordLock)
			{
				if (utcNow - lastDbUpdate < TimeSpan.FromSeconds(1))
					return;
				lastDbUpdate = utcNow;
				UpdateCurrentEntryStatsLocked(utcNow);
			}
		}

		private void UpdateCurrentEntryStatsLocked(DateTime utcNow)
		{
			if (!isRecording || currentEntryId == null || currentFile is null)
				return;

			try
			{
				currentFile.Refresh();
			}
			catch
			{
			}

			var entry = recordingTable.FindById(currentEntryId);
			if (entry is null)
				return;

			entry.FileName = currentFile.Name;
			entry.FileId = BuildFileId(currentFile);
			entry.SizeBytes = currentFile.Exists ? currentFile.Length : entry.SizeBytes;
			entry.IsOpen = true;

			var duration = currentDurationOverride ?? writer?.Duration;
			if (duration.HasValue)
				entry.DurationMs = (long)duration.Value.TotalMilliseconds;

			if (entry.Participants is null || entry.Participants.Count == 0)
				entry.Participants = GetParticipantsSnapshot();
			entry.Waveforms = GetWaveformsSnapshot();

			entry.UpdatedUtc = EnsureUtc(utcNow);
			recordingTable.Update(entry);
		}

		private void UpdateCurrentEntryParticipantsLocked(DateTime utcNow)
		{
			if (!isRecording || currentEntryId == null)
				return;
			var entry = recordingTable.FindById(currentEntryId);
			if (entry is null)
				return;
			entry.Participants = GetParticipantsSnapshot();
			entry.UpdatedUtc = EnsureUtc(utcNow);
			recordingTable.Update(entry);
		}

		private void UpdateCurrentEntryFinal(global::System.IO.FileInfo file, DateTime endTime, TimeSpan duration, string? dbEntryId, List<RecordingWaveformInfo>? waveforms)
		{
			if (dbEntryId == null)
				return;
			var entry = recordingTable.FindById(dbEntryId);
			if (entry is null)
				return;

			entry.FileName = file.Name;
			entry.FileId = BuildFileId(file);
			entry.IsOpen = false;
			entry.EndUtc = EnsureUtc(endTime);
			entry.DurationMs = (long)duration.TotalMilliseconds;
			entry.SizeBytes = file.Exists ? file.Length : entry.SizeBytes;
			entry.UpdatedUtc = UtcNow();
			if (entry.Participants is null || entry.Participants.Count == 0)
				entry.Participants = GetParticipantsSnapshot();
			if (waveforms != null)
				entry.Waveforms = waveforms;
			recordingTable.Update(entry);
		}

		private DirectoryInfo GetRecordingBaseDir()
		{
			var path = config.Recording.Path.Value;
			if (!Path.IsPathRooted(path))
			{
				var baseDir = config.LocalConfigDir ?? config.GetParent().Configs.BotsPath.Value;
				path = Path.Combine(baseDir, path);
			}
			return new DirectoryInfo(path);
		}

		private DirectoryInfo GetRecordingDirForNow()
		{
			var baseDir = GetRecordingBaseDir();
			var dayFolder = UtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			return new DirectoryInfo(Path.Combine(baseDir.FullName, dayFolder));
		}

		private global::System.IO.FileInfo CreateNewRecordingFile(DateTime start)
		{
			start = EnsureUtc(start);
			var outDir = GetRecordingDirForNow();
			outDir.Create();
			var baseName = $"{start:HH-mm-ss}__open.opus";
			var filePath = Path.Combine(outDir.FullName, baseName);
			var file = new global::System.IO.FileInfo(filePath);
			int suffix = 1;
			while (file.Exists)
			{
				filePath = Path.Combine(outDir.FullName, $"{start:HH-mm-ss}__open_{suffix}.opus");
				file = new global::System.IO.FileInfo(filePath);
				suffix++;
			}
			return file;
		}

		private void RotateRecording(DateTime now)
		{
			global::System.IO.FileInfo? newFile = null;
			System.IO.Stream? newStream = null;
			OggOpusWriter? newWriter = null;
			string? newEntryId = null;
			DateTime newStartTime = TrimToSecond(now);

			try
			{
				newFile = CreateNewRecordingFile(newStartTime);
				newStream = newFile.Open(System.IO.FileMode.Create, global::System.IO.FileAccess.Write, global::System.IO.FileShare.Read);
				newWriter = new OggOpusWriter(newStream);

				// Snapshot participants for DB entry (requires lock or safe copy)
				List<RecordingParticipant> participantsSnapshot;
				lock (recordLock)
				{
					participantsSnapshot = GetParticipantsSnapshot();
				}
				newEntryId = CreateRecordingEntry(newFile, newStartTime, participantsSnapshot);

				global::System.IO.FileInfo? oldFile = null;
				OggOpusWriter? oldWriter = null;
				string? oldEntryId = null;
				WaveformSet? oldWaveforms = null;
				TimeSpan oldDuration = TimeSpan.Zero;

				lock (recordLock)
				{
					if (!isRecording)
					{
						// Recording stopped while we were preparing, abort rotation
						throw new OperationCanceledException("Recording stopped during rotation");
					}

					// Capture old state
					oldFile = currentFile;
					oldWriter = writer;
					oldEntryId = currentEntryId;
					oldWaveforms = waveformSet;
					oldDuration = writer?.Duration ?? currentDurationOverride ?? TimeSpan.Zero;

					// Assign new state
					currentFile = newFile;
					startTime = newStartTime;
					writer = newWriter;
					currentEntryId = newEntryId;
					waveformSampleIndex = 0;
					waveformSet = new WaveformSet(currentFile, WaveformSampleRate);
					waveformSet.Tracks[MixedWaveformUid] = CreateWaveformTrack(MixedWaveformUid, MixedWaveformName, currentFile, waveformSampleIndex);
					lastWaveformFlush = DateTime.UtcNow;
					
					if (encoder != null)
						encoder.OutStream = writer;
					
					// Prevent cleanup in finally
					newStream = null; 
					newWriter = null; 
					newFile = default; 
				}

				Log.Info("Recording continued (new segment): {0}", currentFile!.FullName);

				// Finalize old file outside lock
				if (oldWriter != null)
					oldWriter.Dispose();
				
				if (oldFile != null)
					FinalizeCurrentFile(oldFile, TrimToSecond(now), "max duration reached", oldDuration, oldEntryId, oldWaveforms);
			}
			catch (Exception ex)
			{
				if (!(ex is OperationCanceledException))
					Log.Error(ex, "Failed to rotate recording");

				newWriter?.Dispose(); 
				newStream?.Dispose();

				if (newFile != null)
				{
					try { if (newFile.Exists) newFile.Delete(); } catch { }
				}

				if (newEntryId != null)
				{
					try { recordingTable.Delete(newEntryId); } catch { }
				}
			}
		}



		private void RecoverOrphans()
		{
			try
			{
				var baseDir = GetRecordingBaseDir();
				if (!baseDir.Exists)
					return;

				var openFiles = baseDir.GetFiles("*__open.opus", SearchOption.AllDirectories);
				foreach (var file in openFiles)
				{
					// Double check it's not the current file (though on startup it shouldn't be)
					if (isRecording && currentFile != null && file.FullName == currentFile.FullName)
						continue;
					
					// Attempt to read precise duration from Ogg pages
					var duration = ReadOggDuration(file);
					
					// If failed, we might still fallback, but precise is preferred.
					// Pass null for dbEntryId to let FindEntryByFileId resolve it.
					FinalizeCurrentFile(file, file.LastWriteTimeUtc, "crash recovery", duration, null, null);
				}
			}
			catch (Exception ex)
			{
				Log.Warn(ex, "Failed to recover orphaned recordings");
			}
		}

		private TimeSpan? ReadOggDuration(global::System.IO.FileInfo file)
		{
			try
			{
				// Ogg page header: 'OggS' (4 bytes) + version (1) + type (1) + granule (8, LE)
				// We look at the end of the file for the last page.
				using var fs = file.Open(System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
				if (fs.Length < 27) return null;

				byte[] buffer = new byte[Math.Min(fs.Length, 8192)];
				fs.Seek(-buffer.Length, SeekOrigin.End);
				fs.Read(buffer, 0, buffer.Length);

				for (int i = buffer.Length - 14; i >= 0; i--)
				{
					if (buffer[i] == 0x4F && buffer[i + 1] == 0x67 && buffer[i + 2] == 0x67 && buffer[i + 3] == 0x53)
					{
						long granule = BitConverter.ToInt64(buffer, i + 6);
						if (granule > 0)
						{
							// Opus constant: always 48000Hz for Ogg encapsulation granule mapping
							return TimeSpan.FromSeconds(granule / 48000.0);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log.Debug(ex, "Failed to read Ogg duration from file {0}", file.Name);
			}
			return null;
		}

		private void FinalizeCurrentFile(global::System.IO.FileInfo file, DateTime endTime, string? reason, TimeSpan? durationOverride, string? dbEntryId, WaveformSet? waveforms)
		{
			if (!file.Exists)
				return;

			file.Refresh(); // Ensure size/dates are up to date
			
			// Resolve entry if ID not provided
			RecordingEntry? entry = null;
			if (dbEntryId != null)
				entry = recordingTable.FindById(dbEntryId);
			else
				entry = FindEntryByFileId(BuildFileId(file));

			var duration = durationOverride ?? TimeSpan.Zero;
			
			if (duration == TimeSpan.Zero)
			{
				if (entry != null)
				{
					duration = endTime - ToUtc(entry.StartUtc);
				}
				else
				{
					// Try derived from filename/directory
					try 
					{
						// Expected format: parent=yyyy-MM-dd, name=HH-mm-ss__open*.opus
						var timePart = file.Name.Split(new[] { "__" }, StringSplitOptions.None)[0];
						var datePart = file.Directory?.Name; // yyyy-MM-dd
						if (datePart != null && DateTime.TryParseExact($"{datePart} {timePart}", "yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var startUtc))
						{
							duration = endTime - startUtc;
						}
					}
					catch { }
				}
			}
			
			// Sanity check for negative duration
			if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

			if (duration < config.Recording.MinDuration.Value)
			{
				try
				{
					file.Delete();
					DeleteWaveformFiles(file, waveforms);
					if (entry != null)
						recordingTable.Delete(entry.Id);
					
					if (string.IsNullOrWhiteSpace(reason))
						Log.Info("Recording discarded (too short): {0}", duration);
					else
						Log.Info("Recording discarded (too short, {0}): {1}", reason, duration);
				}
				catch (Exception ex)
				{
					Log.Warn(ex, "Failed to delete short recording");
				}
				return;
			}

			// For renaming, we need start time to generate name `startPart__endPart`.
			// The original `BuildFinalPath` uses `file.Name` to extract `startPart`.
			// `CreateNewRecordingFile` names it `${start:HH-mm-ss}__open.opus`
			// `BuildFinalPath` splits by `__`. 
			// So we can derive start time string from filename! Safe.

			var openFileSnapshot = new global::System.IO.FileInfo(file.FullName);
			var (finalPath, _) = BuildFinalPath(file, endTime);
			var finalFile = file;
			try
			{
				var targetPath = ResolveFinalPathCollision(finalPath);
				file.MoveTo(targetPath);
				finalFile = new global::System.IO.FileInfo(targetPath);
			}
			catch (Exception ex)
			{
				Log.Warn(ex, "Failed to finalize recording file");
			}

			var waveformsFinal = FinalizeWaveformFiles(openFileSnapshot, finalFile, waveforms);

			// Use the looked-up entry ID if available
			UpdateCurrentEntryFinal(finalFile, endTime, duration, entry?.Id, waveformsFinal);

			if (string.IsNullOrWhiteSpace(reason))
				Log.Info("Recording stopped: {0}", finalFile.FullName);
			else
				Log.Info("Recording stopped ({0}): {1}", reason, finalFile.FullName);
		}

		private static (string finalPath, string finalName) BuildFinalPath(global::System.IO.FileInfo file, DateTime end)
		{
			end = EnsureUtc(end);
			var name = Path.GetFileNameWithoutExtension(file.Name);
			var parts = name.Split(new[] { "__" }, StringSplitOptions.None);
			var startPart = parts.Length > 0 ? parts[0] : "unknown_start";
			string? extraSuffix = null;
			if (parts.Length > 1 && parts[1].StartsWith("open_", StringComparison.OrdinalIgnoreCase))
				extraSuffix = parts[1].Substring("open_".Length);

			var finalName = extraSuffix is null || extraSuffix.Length == 0
				? $"{startPart}__{end:HH-mm-ss}.opus"
				: $"{startPart}__{end:HH-mm-ss}_{extraSuffix}.opus";
			var finalPath = Path.Combine(file.DirectoryName!, finalName);
			return (finalPath, finalName);
		}

		private static string ResolveFinalPathCollision(string finalPath)
		{
			if (!File.Exists(finalPath))
				return finalPath;
			var dir = Path.GetDirectoryName(finalPath) ?? string.Empty;
			var name = Path.GetFileNameWithoutExtension(finalPath);
			var ext = Path.GetExtension(finalPath);
			int i = 1;
			string candidate;
			do
			{
				candidate = Path.Combine(dir, $"{name}_{i}{ext}");
				i++;
			}
			while (File.Exists(candidate));
			return candidate;
		}

		private void EnforceMaxSize()
		{
			if (!TextUtil.TryParseBytes(config.Recording.MaxTotalSize.Value, out var maxSize) || maxSize <= 0)
				return;

			var baseDir = GetRecordingBaseDir();
			if (!baseDir.Exists)
				return;

			var files = baseDir.GetFiles("*.opus", SearchOption.AllDirectories)
				.OrderBy(f => f.LastWriteTimeUtc)
				.ToList();

			long total = files.Sum(f => f.Length);
			int idx = 0;
			while (total > maxSize && idx < files.Count)
			{
				var file = files[idx++];
				try
				{
					total -= file.Length;
					file.Delete();
					DeleteEntryByFile(file);
					CleanupEmptyDirs(file.Directory);
				}
				catch (Exception ex)
				{
					Log.Warn(ex, "Failed to delete old recording file");
				}
			}
		}

		private static void CleanupEmptyDirs(DirectoryInfo? dir)
		{
			while (dir != null && dir.Exists)
			{
				if (dir.EnumerateFileSystemInfos().Any())
					break;
				var parent = dir.Parent;
				dir.Delete();
				dir = parent;
			}
		}

		private void RefreshParticipantsSnapshot()
		{
			var self = ts3FullClient.Book.Self();
			if (self is null || !self.Uid.HasValue)
				return;
			var ownChannel = self.Channel;
			var selfUid = self.Uid.Value;
			var clients = ts3FullClient.Book.Clients.Values.Where(c => c.Channel == ownChannel && c != self && c.Uid != selfUid).ToList();
			foreach (var client in clients)
			{
				if (client.Uid is null)
					continue;
				participants[client.Uid.Value] = client.Name.ToString();
			}
		}

		private List<RecordingParticipant> GetParticipantsSnapshot()
		{
			return participants.Select(p => new RecordingParticipant { Uid = p.Key.Value, Name = p.Value }).ToList();
		}

		private List<RecordingWaveformInfo> GetWaveformsSnapshot()
		{
			if (waveformSet is null || waveformSet.Tracks.Count == 0)
				return new List<RecordingWaveformInfo>();
			var list = new List<RecordingWaveformInfo>(waveformSet.Tracks.Count);
			foreach (var track in waveformSet.Tracks.Values)
			{
				long size = 0;
				try
				{
					track.File.Refresh();
					if (track.File.Exists)
						size = track.File.Length;
				}
				catch
				{
					size = 0;
				}
				list.Add(new RecordingWaveformInfo
				{
					Uid = track.Uid.Value,
					Name = track.Name,
					SampleRate = track.SampleRate,
					Samples = track.Samples,
					MaxSample = track.MaxSample,
					SizeBytes = size,
					FileId = BuildFileId(track.File)
				});
			}
			return list;
		}

		private static byte NormalizeRmsToByte(double rms)
		{
			if (double.IsNaN(rms) || double.IsInfinity(rms))
				return 0;
			var value = (int)Math.Round(rms * 255.0);
			if (value < 0) return 0;
			if (value > 255) return 255;
			return (byte)value;
		}

		private bool TryGetClientIdentity(ClientId sender, out Uid uid, out string name)
		{
			uid = Uid.Null;
			name = string.Empty;
			if (!ts3FullClient.Book.Clients.TryGetValue(sender, out var client) || client.Uid is null)
				return false;
			uid = client.Uid.Value;
			name = client.Name.ToString();
			return true;
		}

		private void AppendWaveformSamplesLocked(Dictionary<Uid, WaveformSample>? samples, DateTime utcNow)
		{
			if (waveformSet is null)
				return;

			if (samples != null)
			{
				foreach (var kvp in samples)
				{
					if (!waveformSet.Tracks.TryGetValue(kvp.Key, out var track))
					{
						track = CreateWaveformTrack(kvp.Key, kvp.Value.Name, waveformSet.AudioFile, waveformSampleIndex);
						waveformSet.Tracks[kvp.Key] = track;
					}
					else if (!string.IsNullOrWhiteSpace(kvp.Value.Name))
					{
						track.Name = kvp.Value.Name;
					}
				}
			}

			foreach (var track in waveformSet.Tracks.Values)
			{
				if (samples != null && samples.TryGetValue(track.Uid, out var sample))
					track.Append(sample.Value);
				else
					track.Append(0);
			}

			waveformSampleIndex++;

			if (utcNow - lastWaveformFlush >= WaveformFlushInterval)
			{
				lastWaveformFlush = utcNow;
				foreach (var track in waveformSet.Tracks.Values)
					track.Flush();
			}
		}

		private WaveformTrack CreateWaveformTrack(Uid uid, string name, global::System.IO.FileInfo audioFile, int initialSamples)
		{
			var safeUid = EncodeUidForFileName(uid.Value);
			var baseName = Path.GetFileNameWithoutExtension(audioFile.Name);
			var fileName = BuildWaveformFileName(baseName, safeUid);
			var path = Path.Combine(audioFile.DirectoryName ?? string.Empty, fileName);
			var file = new global::System.IO.FileInfo(path);
			var stream = file.Open(System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.Read);
			WriteWaveformHeader(stream, WaveformSampleRate, 0);

			var track = new WaveformTrack(uid, name, safeUid, file, stream, WaveformSampleRate);
			if (initialSamples > 0)
				track.WriteZeros(initialSamples);
			return track;
		}

		private static string BuildWaveformFileName(string audioBaseName, string safeUid)
			=> $"{audioBaseName}__{safeUid}.wfm";

		private static string EncodeUidForFileName(string uid)
			=> Uri.EscapeDataString(uid);

		private static string DecodeUidFromFileName(string token)
		{
			try { return Uri.UnescapeDataString(token); }
			catch { return token; }
		}

		private static void WriteWaveformHeader(Stream stream, int sampleRate, int sampleCount)
		{
			Span<byte> header = stackalloc byte[WaveformHeaderSize];
			header[0] = (byte)'T';
			header[1] = (byte)'S';
			header[2] = (byte)'W';
			header[3] = (byte)'F';
			header[4] = 1; // version
			header[5] = 0; // flags
			header[6] = 0;
			header[7] = 0;
			BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), (uint)sampleRate);
			BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(12, 4), (uint)sampleCount);
			stream.Seek(0, SeekOrigin.Begin);
			stream.Write(header);
		}

		private List<RecordingWaveformInfo> FinalizeWaveformFiles(global::System.IO.FileInfo openAudioFile, global::System.IO.FileInfo finalAudioFile, WaveformSet? waveforms)
		{
			var results = new List<RecordingWaveformInfo>();
			var finalBaseName = Path.GetFileNameWithoutExtension(finalAudioFile.Name);

			if (waveforms != null && waveforms.Tracks.Count > 0)
			{
				foreach (var track in waveforms.Tracks.Values)
				{
					try
					{
						track.FinalizeHeader();
						track.Dispose();
						var targetPath = Path.Combine(track.File.DirectoryName ?? string.Empty, BuildWaveformFileName(finalBaseName, track.SafeUid));
						var resolvedTarget = ResolveFinalPathCollision(targetPath);
						track.File.MoveTo(resolvedTarget);
						var finalFile = new global::System.IO.FileInfo(resolvedTarget);
						results.Add(new RecordingWaveformInfo
						{
							Uid = track.Uid.Value,
							Name = track.Name,
							SampleRate = track.SampleRate,
							Samples = track.Samples,
							MaxSample = track.MaxSample,
							SizeBytes = finalFile.Exists ? finalFile.Length : 0,
							FileId = BuildFileId(finalFile)
						});
					}
					catch (Exception ex)
					{
						Log.Warn(ex, "Failed to finalize waveform file");
					}
				}

				return results;
			}

			// Fallback: scan orphaned waveform files (e.g., crash recovery)
			if (openAudioFile.Directory is null || !openAudioFile.Directory.Exists)
				return results;

			var openBaseName = Path.GetFileNameWithoutExtension(openAudioFile.Name);
			var pattern = $"{openBaseName}__*.wfm";
			foreach (var file in openAudioFile.Directory.GetFiles(pattern, SearchOption.TopDirectoryOnly))
			{
				try
				{
					var uidToken = Path.GetFileNameWithoutExtension(file.Name).Substring(openBaseName.Length + 2);
					var uid = DecodeUidFromFileName(uidToken);
					RepairWaveformHeader(file);
					var targetPath = Path.Combine(file.DirectoryName ?? string.Empty, BuildWaveformFileName(finalBaseName, uidToken));
					var resolvedTarget = ResolveFinalPathCollision(targetPath);
					file.MoveTo(resolvedTarget);
					var finalFile = new global::System.IO.FileInfo(resolvedTarget);
					var sampleCount = GetWaveformSampleCount(finalFile);
					var maxSample = ReadWaveformMaxSample(finalFile);
					results.Add(new RecordingWaveformInfo
					{
						Uid = uid,
						Name = string.Empty,
						SampleRate = WaveformSampleRate,
						Samples = sampleCount,
						MaxSample = maxSample,
						SizeBytes = finalFile.Exists ? finalFile.Length : 0,
						FileId = BuildFileId(finalFile)
					});
				}
				catch (Exception ex)
				{
					Log.Warn(ex, "Failed to recover waveform file");
				}
			}

			return results;
		}

		private void DeleteWaveformFiles(global::System.IO.FileInfo audioFile, WaveformSet? waveforms)
		{
			if (waveforms != null && waveforms.Tracks.Count > 0)
			{
				foreach (var track in waveforms.Tracks.Values)
				{
					try
					{
						track.Dispose();
						if (track.File.Exists)
							track.File.Delete();
					}
					catch (Exception ex)
					{
						Log.Warn(ex, "Failed to delete waveform file");
					}
				}
				CleanupEmptyDirs(audioFile.Directory);
				return;
			}

			if (audioFile.Directory is null || !audioFile.Directory.Exists)
				return;

			var baseName = Path.GetFileNameWithoutExtension(audioFile.Name);
			foreach (var file in audioFile.Directory.GetFiles($"{baseName}__*.wfm", SearchOption.TopDirectoryOnly))
			{
				try { file.Delete(); } catch { }
			}
			CleanupEmptyDirs(audioFile.Directory);
		}

		private static void RepairWaveformHeader(global::System.IO.FileInfo file)
		{
			try
			{
				if (!file.Exists)
					return;
				var sampleCount = GetWaveformSampleCount(file);
				using var stream = file.Open(System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.ReadWrite);
				WriteWaveformHeader(stream, WaveformSampleRate, sampleCount);
				stream.Flush();
			}
			catch
			{
			}
		}

		private static int GetWaveformSampleCount(global::System.IO.FileInfo file)
		{
			try
			{
				file.Refresh();
				if (!file.Exists || file.Length <= WaveformHeaderSize)
					return 0;
				var payload = file.Length - WaveformHeaderSize;
				return payload > int.MaxValue ? int.MaxValue : (int)payload;
			}
			catch
			{
				return 0;
			}
		}

		private static int ReadWaveformMaxSample(global::System.IO.FileInfo file)
		{
			try
			{
				if (!file.Exists || file.Length <= WaveformHeaderSize)
					return 0;
				using var stream = file.Open(System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
				stream.Seek(WaveformHeaderSize, SeekOrigin.Begin);
				var buffer = new byte[8192];
				int read;
				int max = 0;
				while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
				{
					for (int i = 0; i < read; i++)
					{
						if (buffer[i] > max)
							max = buffer[i];
					}
				}
				return max;
			}
			catch
			{
				return 0;
			}
		}

		private static string? NormalizeFilter(string? value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var trimmed = value.Trim();
			if (trimmed == "-" || trimmed == "_" || trimmed.Equals("null", StringComparison.OrdinalIgnoreCase))
				return null;
			return trimmed;
		}

		private static List<string> ParseFilterTokens(string? value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return new List<string>();
			return value
				.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(v => v.Trim())
				.Where(v => !string.IsNullOrWhiteSpace(v))
				.Select(v => v.ToLowerInvariant())
				.Distinct()
				.ToList();
		}

		private static bool MatchesParticipantFilter(List<RecordingParticipant>? participantsList, List<string> nameTokens, List<string> uidTokens)
		{
			if (nameTokens.Count == 0 && uidTokens.Count == 0)
				return true;
			if (participantsList == null || participantsList.Count == 0)
				return false;

			bool nameMatch = nameTokens.Count == 0;
			bool uidMatch = uidTokens.Count == 0;

			foreach (var participant in participantsList)
			{
				if (!nameMatch && !string.IsNullOrWhiteSpace(participant.Name))
				{
					var lower = participant.Name.ToLowerInvariant();
					if (nameTokens.Any(t => lower.Contains(t)))
						nameMatch = true;
				}
				if (!uidMatch && !string.IsNullOrWhiteSpace(participant.Uid))
				{
					var uidLower = participant.Uid.ToLowerInvariant();
					if (uidTokens.Any(t => uidLower == t))
						uidMatch = true;
				}
				if (nameMatch && uidMatch)
					return true;
			}

			return nameMatch && uidMatch;
		}

		private global::System.IO.FileInfo? ResolveRecordingFile(string id)
		{
			var baseDir = GetRecordingBaseDir();
			var fullPath = Path.GetFullPath(Path.Combine(baseDir.FullName, id));
			var basePath = baseDir.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
				return null;
			var file = new global::System.IO.FileInfo(fullPath);
			return file.Exists ? file : null;
		}

		private global::System.IO.FileInfo? ResolveWaveformFile(string id, string uid)
		{
			if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(uid))
				return null;

			lock (recordLock)
			{
				if (currentFile != null && BuildFileId(currentFile) == id && waveformSet != null)
				{
					var uidKey = new Uid(uid);
					if (waveformSet.Tracks.TryGetValue(uidKey, out var track))
						return track.File;
				}
			}

			var entry = FindEntryByFileId(id);
			var waveforms = entry?.Waveforms;
			if (waveforms != null)
			{
				var info = waveforms.FirstOrDefault(w => string.Equals(w.Uid, uid, StringComparison.Ordinal));
				if (info != null)
					return ResolveRecordingFile(info.FileId);
			}

			return null;
		}

		private static RecordingInfo BuildRecordingInfo(global::System.IO.FileInfo? file, DateTime start, DateTime? end, bool isOpen, TimeSpan? durationOverride = null, List<RecordingParticipant>? participants = null, List<RecordingWaveformInfo>? waveforms = null)
		{
			long size = 0;
			if (file != null)
			{
				try
				{
					file.Refresh();
					if (file.Exists)
						size = file.Length;
				}
				catch
				{
					size = 0;
				}
			}
			start = TrimToSecond(start);
			if (end.HasValue)
				end = TrimToSecond(end.Value);
			var startOffset = ToOffset(start);
			DateTimeOffset? endOffset = end.HasValue ? ToOffset(end.Value) : (DateTimeOffset?)null;
			var duration = durationOverride ?? (endOffset.HasValue ? (endOffset.Value - startOffset) : (TimeSpan?)null);
			var id = file != null ? Path.Combine(file.Directory?.Name ?? string.Empty, file.Name) : string.Empty;
			return new RecordingInfo
			{
				Id = id.Replace(Path.DirectorySeparatorChar, '/'),
				Start = startOffset,
				End = endOffset,
				Size = size,
				Duration = duration,
				IsOpen = isOpen,
				Participants = participants,
				Waveforms = waveforms
			};
		}

		private static RecordingInfo BuildRecordingInfo(RecordingEntry entry)
		{
			var startUtc = EnsureUtc(entry.StartUtc);
			var start = new DateTimeOffset(startUtc, TimeSpan.Zero);
			DateTimeOffset? end = null;
			if (entry.EndUtc.HasValue)
			{
				var endUtc = EnsureUtc(entry.EndUtc.Value);
				end = new DateTimeOffset(endUtc, TimeSpan.Zero);
			}
			TimeSpan? duration = entry.DurationMs.HasValue ? TimeSpan.FromMilliseconds(entry.DurationMs.Value) : (TimeSpan?)null;
			if (entry.IsOpen && !duration.HasValue)
				duration = DateTimeOffset.UtcNow - start;
			return new RecordingInfo
			{
				Id = entry.FileId,
				Start = start,
				End = end,
				Size = entry.SizeBytes,
				Duration = duration,
				IsOpen = entry.IsOpen,
				Participants = entry.Participants,
				Waveforms = entry.Waveforms
			};
		}

		private static DateTime TrimToSecond(DateTime value)
		{
			return new DateTime(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), value.Kind);
		}

		private static DateTimeOffset TrimToSecond(DateTimeOffset value)
		{
			return new DateTimeOffset(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), value.Offset);
		}

		private static DateTimeOffset ToOffset(DateTime value)
		{
			if (value.Kind == DateTimeKind.Unspecified)
				value = DateTime.SpecifyKind(value, DateTimeKind.Local);
			return new DateTimeOffset(value);
		}

		private static DateTime ToUtc(DateTime value)
			=> EnsureUtc(value);

		private static DateTime UtcNow()
			=> DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

		private static DateTime EnsureUtc(DateTime value)
		{
			if (value.Kind == DateTimeKind.Utc)
				return value;
			if (value.Kind == DateTimeKind.Local)
				return value.ToUniversalTime();
			return DateTime.SpecifyKind(value, DateTimeKind.Utc);
		}

		private sealed class RecordingSink : IAudioPassiveConsumer
		{
			private readonly RecordingManager manager;
			public bool Active => true;

			public RecordingSink(RecordingManager manager) => this.manager = manager;

			public void Write(Span<byte> data, Meta? meta)
			{
				if (meta is null)
					return;
				manager.PushPcm(meta.In.Sender, data);
			}
		}

		private sealed class PcmBuffer
		{
			private readonly Queue<ArraySegment<byte>> segments = new Queue<ArraySegment<byte>>();
			private int headOffset;
			private long lastWriteTicks = DateTime.UtcNow.Ticks;
			// NOTE: This property is approximate, for precise checks use ReadLastWrite()
			public DateTime LastWrite => new DateTime(System.Threading.Interlocked.Read(ref lastWriteTicks), DateTimeKind.Utc);

			public DateTime ReadLastWrite()
			{
				return new DateTime(System.Threading.Interlocked.Read(ref lastWriteTicks), DateTimeKind.Utc);
			}

			public void Write(ReadOnlySpan<byte> data)
			{
				if (data.Length == 0)
					return;
				var buffer = data.ToArray();
				segments.Enqueue(new ArraySegment<byte>(buffer));
				System.Threading.Interlocked.Exchange(ref lastWriteTicks, DateTime.UtcNow.Ticks);
			}

			public bool ReadFrame(byte[] destination, int length)
			{
				if (length <= 0)
					return false;
				int written = 0;
				while (written < length && segments.Count > 0)
				{
					var seg = segments.Peek();
					int available = seg.Count - headOffset;
					int toCopy = Math.Min(length - written, available);
					Buffer.BlockCopy(seg.Array!, seg.Offset + headOffset, destination, written, toCopy);
					written += toCopy;
					headOffset += toCopy;
					if (headOffset >= seg.Count)
					{
						segments.Dequeue();
						headOffset = 0;
					}
				}
				if (written < length)
					Array.Clear(destination, written, length - written);
				return written > 0;
			}
		}

		private readonly struct WaveformSample
		{
			public WaveformSample(byte value, string name, double rms)
			{
				Value = value;
				Name = name;
				Rms = rms;
			}

			public byte Value { get; }
			public string Name { get; }
			public double Rms { get; }
		}

		private sealed class WaveformSet
		{
			public WaveformSet(global::System.IO.FileInfo audioFile, int sampleRate)
			{
				AudioFile = audioFile;
				SampleRate = sampleRate;
				Tracks = new Dictionary<Uid, WaveformTrack>();
			}

			public global::System.IO.FileInfo AudioFile { get; }
			public int SampleRate { get; }
			public Dictionary<Uid, WaveformTrack> Tracks { get; }
		}

		private sealed class WaveformTrack : IDisposable
		{
			private readonly List<byte> pending = new List<byte>(256);

			public WaveformTrack(Uid uid, string name, string safeUid, global::System.IO.FileInfo file, System.IO.FileStream stream, int sampleRate)
			{
				Uid = uid;
				Name = name;
				SafeUid = safeUid;
				File = file;
				Stream = stream;
				SampleRate = sampleRate;
			}

			public Uid Uid { get; }
			public string Name { get; set; }
			public string SafeUid { get; }
			public global::System.IO.FileInfo File { get; }
			public System.IO.FileStream Stream { get; }
			public int SampleRate { get; }
			public int Samples { get; private set; }
			public byte MaxSample { get; private set; }

			public void Append(byte value)
			{
				pending.Add(value);
				Samples++;
				if (value > MaxSample)
					MaxSample = value;
			}

			public void WriteZeros(int count)
			{
				if (count <= 0)
					return;
				Flush();
				var buffer = new byte[4096];
				int remaining = count;
				while (remaining > 0)
				{
					int chunk = Math.Min(buffer.Length, remaining);
					Stream.Write(buffer, 0, chunk);
					remaining -= chunk;
					Samples += chunk;
				}
				Stream.Flush();
			}

			public void Flush()
			{
				if (pending.Count == 0)
					return;
				var buffer = pending.ToArray();
				Stream.Write(buffer, 0, buffer.Length);
				pending.Clear();
				Stream.Flush();
			}

			public void FinalizeHeader()
			{
				Flush();
				WriteWaveformHeader(Stream, SampleRate, Samples);
				Stream.Flush();
			}

			public void Dispose()
			{
				try { Stream.Dispose(); } catch { }
			}
		}
	}

	public class RecordingEntry
	{
		[BsonId]
		public string Id { get; set; } = string.Empty;
		public int BotId { get; set; }
		public string FileId { get; set; } = string.Empty;
		public string FileName { get; set; } = string.Empty;
		public DateTime StartUtc { get; set; }
		public DateTime? EndUtc { get; set; }
		public long SizeBytes { get; set; }
		public long? DurationMs { get; set; }
		public bool IsOpen { get; set; }
		public List<RecordingParticipant> Participants { get; set; } = new List<RecordingParticipant>();
		public List<RecordingWaveformInfo> Waveforms { get; set; } = new List<RecordingWaveformInfo>();
		public DateTime CreatedUtc { get; set; }
		public DateTime UpdatedUtc { get; set; }
	}

	public class RecordingInfo
	{
		public string Id { get; set; } = string.Empty;
		public DateTimeOffset Start { get; set; }
		public DateTimeOffset? End { get; set; }
		public long Size { get; set; }
		public TimeSpan? Duration { get; set; }
		public bool IsOpen { get; set; }
		public List<RecordingParticipant>? Participants { get; set; }
		public List<RecordingWaveformInfo>? Waveforms { get; set; }
	}

	public class RecordingParticipant
	{
		public string Uid { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;
	}

	public class RecordingWaveformInfo
	{
		public string Uid { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;
		public int SampleRate { get; set; }
		public int Samples { get; set; }
		public int MaxSample { get; set; }
		public long SizeBytes { get; set; }
		public string FileId { get; set; } = string.Empty;
	}

	public class RecordingStatus
	{
		public bool Enabled { get; set; }
		public bool Active { get; set; }
		public RecordingInfo? Current { get; set; }
	}
}
