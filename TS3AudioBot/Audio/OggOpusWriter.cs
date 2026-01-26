// TS3AudioBot - An advanced Musicbot for Teamspeak 3
// Copyright (C) 2017  TS3AudioBot contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TSLib.Audio;
using TSLib.Helper;

namespace TS3AudioBot.Audio
{
	public sealed class OggOpusWriter : IAudioPassiveConsumer, IDisposable
	{
		public struct OggPageInfo
		{
			public byte[] Data { get; }
			public long Granule { get; }
			public bool IsHeader { get; }

			public OggPageInfo(byte[] data, long granule, bool isHeader)
			{
				Data = data;
				Granule = granule;
				IsHeader = isHeader;
			}
		}

		public Action<OggPageInfo>? PageWritten { get; set; }
		private static readonly uint[] CrcTable = BuildCrcTable();
		private readonly Stream stream;
		private readonly uint serial;
		private readonly int channels;
		private readonly int sampleRate;
		private readonly int preSkip;
		private readonly int samplesPerPacket;
		private readonly List<byte> pageData = new List<byte>(4096);
		private readonly List<byte> pageSegments = new List<byte>(255);
		private uint sequence;
		private long granulePos;
		private bool disposed;

		public bool Active => !disposed;
		public long TotalSamples => granulePos;
		public TimeSpan Duration => TimeSpan.FromSeconds(granulePos / (double)sampleRate);

		public OggOpusWriter(Stream stream, int sampleRate = 48000, int channels = 2, int preSkip = 312, int samplesPerPacket = 960, string? vendor = null)
		{
			if (stream is null)
				throw new ArgumentNullException(nameof(stream));
			if (sampleRate <= 0)
				throw new ArgumentOutOfRangeException(nameof(sampleRate));
			if (channels != 1 && channels != 2)
				throw new ArgumentOutOfRangeException(nameof(channels));
			if (preSkip < 0)
				throw new ArgumentOutOfRangeException(nameof(preSkip));

			this.stream = stream;
			this.sampleRate = sampleRate;
			this.channels = channels;
			this.preSkip = preSkip;
			this.samplesPerPacket = samplesPerPacket;
			serial = (uint)Tools.Random.Next(int.MinValue, int.MaxValue);
			vendor ??= "TS3AudioBot";
			WriteHeaders(vendor);
		}

		public void Write(Span<byte> data, Meta? meta)
		{
			if (disposed || data.IsEmpty)
				return;
			WritePacket(data);
		}

		public void Dispose()
		{
			if (disposed)
				return;
			disposed = true;
			FlushPage(eos: true);
			stream.Flush();
			stream.Dispose();
		}

		public void Flush()
		{
			if (disposed)
				return;
			FlushPage();
			stream.Flush();
		}

		private void WriteHeaders(string vendor)
		{
			var head = BuildOpusHead();
			WritePage(head, bos: true, eos: false, granule: 0, isHeader: true);

			var tags = BuildOpusTags(vendor);
			WritePage(tags, bos: false, eos: false, granule: 0, isHeader: true);
		}

		private byte[] BuildOpusHead()
		{
			var buf = new byte[19];
			Encoding.ASCII.GetBytes("OpusHead", 0, 8, buf, 0);
			buf[8] = 1; // version
			buf[9] = (byte)channels;
			WriteLe16(buf, 10, (ushort)preSkip);
			WriteLe32(buf, 12, (uint)sampleRate);
			WriteLe16(buf, 16, 0); // output gain
			buf[18] = 0; // channel mapping
			return buf;
		}

		private static byte[] BuildOpusTags(string vendor)
		{
			var vendorBytes = Encoding.UTF8.GetBytes(vendor);
			var len = 8 + 4 + vendorBytes.Length + 4;
			var buf = new byte[len];
			Encoding.ASCII.GetBytes("OpusTags", 0, 8, buf, 0);
			WriteLe32(buf, 8, (uint)vendorBytes.Length);
			Buffer.BlockCopy(vendorBytes, 0, buf, 12, vendorBytes.Length);
			WriteLe32(buf, 12 + vendorBytes.Length, 0);
			return buf;
		}

		private void WritePacket(ReadOnlySpan<byte> packet)
		{
			int neededSegments = (packet.Length + 254) / 255;
			if (neededSegments > 255)
				throw new InvalidOperationException("Opus packet too large for single Ogg page.");

			if (pageSegments.Count + neededSegments > 255)
				FlushPage();

			int remaining = packet.Length;
			int offset = 0;
			while (remaining > 0)
			{
				int seg = Math.Min(255, remaining);
				pageSegments.Add((byte)seg);
				pageData.AddRange(packet.Slice(offset, seg).ToArray());
				remaining -= seg;
				offset += seg;
			}

			granulePos += GetPacketSampleCount(packet);

			if (packet.Length > 0 && packet.Length % 255 == 0)
			{
				if (pageSegments.Count + 1 > 255)
					FlushPage();
				pageSegments.Add(0);
			}

			if (pageSegments.Count >= 255)
				FlushPage();
		}

		private int GetPacketSampleCount(ReadOnlySpan<byte> packet)
		{
			if (packet.Length == 0)
				return 0;

			int toc = packet[0];
			int config = toc >> 3;
			int c = toc & 3;

			int frameCount;
			if (c == 0) frameCount = 1;
			else if (c == 1 || c == 2) frameCount = 2;
			else // c == 3
			{
				if (packet.Length < 2) return samplesPerPacket;
				frameCount = packet[1] & 0x3F;
			}

			int frameSize; // samples at 48kHz
			if (config < 12)
			{
				// 10, 20, 40, 60
				frameSize = (config % 4) switch
				{
					0 => 480,
					1 => 960,
					2 => 1920,
					3 => 2880,
					_ => 0
				};
			}
			else if (config < 16)
			{
				// 10, 20
				frameSize = (config % 2) == 0 ? 480 : 960;
			}
			else
			{
				// 2.5, 5, 10, 20
				frameSize = ((config - 16) % 4) switch
				{
					0 => 120,
					1 => 240,
					2 => 480,
					3 => 960,
					_ => 0
				};
			}

			if (frameSize == 0) return samplesPerPacket;
			return frameCount * frameSize;
		}

		private void FlushPage(bool eos = false)
		{
			if (pageSegments.Count == 0 && !eos)
				return;

			WritePage(pageData.ToArray(), bos: false, eos: eos, granule: granulePos, pageSegments.ToArray(), isHeader: false);
			pageData.Clear();
			pageSegments.Clear();
		}

		private void WritePage(byte[] payload, bool bos, bool eos, long granule, bool isHeader)
			=> WritePage(payload, bos, eos, granule, BuildLacing(payload.Length), isHeader);

		private void WritePage(byte[] payload, bool bos, bool eos, long granule, byte[] segments, bool isHeader)
		{
			byte headerType = 0;
			if (bos) headerType |= 0x02;
			if (eos) headerType |= 0x04;

			byte[] header = new byte[27 + segments.Length];
			Encoding.ASCII.GetBytes("OggS", 0, 4, header, 0);
			header[4] = 0; // version
			header[5] = headerType;
			WriteLe64(header, 6, (ulong)granule);
			WriteLe32(header, 14, serial);
			WriteLe32(header, 18, sequence++);
			WriteLe32(header, 22, 0); // checksum placeholder
			header[26] = (byte)segments.Length;
			Buffer.BlockCopy(segments, 0, header, 27, segments.Length);

			uint crc = CalculateCrc(header, payload);
			WriteLe32(header, 22, crc);

			stream.Write(header, 0, header.Length);
			if (payload.Length > 0)
				stream.Write(payload, 0, payload.Length);

			if (PageWritten != null)
			{
				var page = new byte[header.Length + payload.Length];
				Buffer.BlockCopy(header, 0, page, 0, header.Length);
				if (payload.Length > 0)
					Buffer.BlockCopy(payload, 0, page, header.Length, payload.Length);
				PageWritten(new OggPageInfo(page, granule, isHeader));
			}
		}

		private static byte[] BuildLacing(int length)
		{
			int segments = (length + 255) / 255; // Always at least one segment if not handling 0 specifically, but Ogg lacing for length 0 is 1 segment of 0.
			if (length > 0 && length % 255 == 0) segments++; 

			var lacing = new byte[segments];
			int remaining = length;
			int i = 0;
			while (remaining >= 255)
			{
				lacing[i++] = 255;
				remaining -= 255;
			}
			lacing[i] = (byte)remaining;
			return lacing;
		}

		private static uint CalculateCrc(byte[] header, byte[] payload)
		{
			uint crc = 0;
			foreach (var b in header)
				crc = (crc << 8) ^ CrcTable[((crc >> 24) & 0xff) ^ b];
			foreach (var b in payload)
				crc = (crc << 8) ^ CrcTable[((crc >> 24) & 0xff) ^ b];
			return crc;
		}

		private static uint[] BuildCrcTable()
		{
			const uint poly = 0x04c11db7;
			var table = new uint[256];
			for (uint i = 0; i < table.Length; i++)
			{
				uint r = i << 24;
				for (int j = 0; j < 8; j++)
					r = (r << 1) ^ ((r & 0x80000000) != 0 ? poly : 0);
				table[i] = r;
			}
			return table;
		}

		private static void WriteLe16(byte[] buffer, int offset, ushort value)
		{
			buffer[offset] = (byte)(value & 0xff);
			buffer[offset + 1] = (byte)((value >> 8) & 0xff);
		}

		private static void WriteLe32(byte[] buffer, int offset, uint value)
		{
			buffer[offset] = (byte)(value & 0xff);
			buffer[offset + 1] = (byte)((value >> 8) & 0xff);
			buffer[offset + 2] = (byte)((value >> 16) & 0xff);
			buffer[offset + 3] = (byte)((value >> 24) & 0xff);
		}

		private static void WriteLe64(byte[] buffer, int offset, ulong value)
		{
			WriteLe32(buffer, offset, (uint)(value & 0xffffffff));
			WriteLe32(buffer, offset + 4, (uint)((value >> 32) & 0xffffffff));
		}
	}
}
