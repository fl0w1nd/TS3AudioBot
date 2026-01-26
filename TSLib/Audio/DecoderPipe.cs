// TSLib - A free TeamSpeak 3 and 5 client library
// Copyright (C) 2017  TSLib contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

using System;
using System.Collections.Generic;
using TSLib.Audio.Opus;

namespace TSLib.Audio
{
	public class DecoderPipe : IAudioPipe, IDisposable, ISampleInfo
	{
		private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();
		public bool Active => OutStream?.Active ?? false;
		public IAudioPassiveConsumer? OutStream { get; set; }

		public int SampleRate { get; } = 48_000;
		public int Channels { get; } = 2;
		public int BitsPerSample { get; } = 16;

		// TOOO:
		// - Add some sort of decoder reuse to reduce concurrent amount of decoders (see ctl 'reset')
		// - Clean up decoders after some time (Control: Tick?)
		// - Make dispose threadsafe OR redefine thread safety requirements for pipes.

		private readonly Dictionary<ClientId, (OpusDecoder, Codec)> decoders = new Dictionary<ClientId, (OpusDecoder, Codec)>();
		private readonly byte[] decodedBuffer;

		public DecoderPipe()
		{
			decodedBuffer = new byte[4096 * 2];
		}

		public void Write(Span<byte> data, Meta? meta)
		{
			if (OutStream is null || meta?.Codec is null)
				return;
			if (data.Length < 2)
			{
				Log.Debug("Opus packet too small from client {0} ({1}). Dropping packet.", meta.In.Sender, meta.Codec.Value);
				return;
			}

			switch (meta.Codec.Value)
			{
			case Codec.OpusVoice:
				{
					Span<byte> decodedData;
					try
					{
						var decoder = GetDecoder(meta.In.Sender, Codec.OpusVoice);
						decodedData = decoder.Decode(data, decodedBuffer.AsSpan(0, decodedBuffer.Length / 2));
					}
					catch (Exception ex)
					{
						Log.Debug(ex, "Opus decode failed for client {0} (voice). Dropping packet.", meta.In.Sender);
						ResetDecoder(meta.In.Sender);
						return;
					}
					int dataLength = decodedData.Length;
					if (!AudioTools.TryMonoToStereo(decodedBuffer, ref dataLength))
						break;
					OutStream?.Write(decodedBuffer.AsSpan(0, dataLength), meta);
				}
				break;

			case Codec.OpusMusic:
				{
					Span<byte> decodedData;
					try
					{
						var decoder = GetDecoder(meta.In.Sender, Codec.OpusMusic);
						decodedData = decoder.Decode(data, decodedBuffer);
					}
					catch (Exception ex)
					{
						Log.Debug(ex, "Opus decode failed for client {0} (music). Dropping packet.", meta.In.Sender);
						ResetDecoder(meta.In.Sender);
						return;
					}
					OutStream?.Write(decodedData, meta);
				}
				break;

			default:
				// Cannot decode
				break;
			}
		}

		private OpusDecoder GetDecoder(ClientId sender, Codec codec)
		{
			if (decoders.TryGetValue(sender, out var decoder))
			{
				if (decoder.Item2 == codec)
					return decoder.Item1;
				else
					decoder.Item1.Dispose();
			}

			var newDecoder = CreateDecoder(codec);
			decoders[sender] = (newDecoder, codec);
			return newDecoder;
		}

		private void ResetDecoder(ClientId sender)
		{
			if (decoders.TryGetValue(sender, out var decoder))
			{
				decoder.Item1.Dispose();
				decoders.Remove(sender);
			}
		}

		private OpusDecoder CreateDecoder(Codec codec)
		{
			return codec switch
			{
				Codec.OpusVoice => OpusDecoder.Create(SampleRate, 1),
				Codec.OpusMusic => OpusDecoder.Create(SampleRate, 2),
				_ => throw new NotSupportedException(),
			};
		}

		public void Dispose()
		{
			foreach (var (decoder, _) in decoders.Values)
			{
				decoder.Dispose();
			}
		}
	}
}
