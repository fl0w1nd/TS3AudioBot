// Bot status enum
export enum BotStatus {
  Offline = 0,
  Connecting = 1,
  Connected = 2,
}

// Repeat mode enum
export enum RepeatKind {
  Off = 0,
  One = 1,
  All = 2,
}

// Target send mode enum
export enum TargetSendMode {
  None = 0,
  Voice = 1,
  Whisper = 2,
  WhisperGroup = 3,
}

// API response types
export interface ApiError {
  ErrorCode: number
  ErrorMessage: string
  ErrorName: string
  HelpLink?: string
}

export interface CmdBotInfo {
  Id: number | null
  Name: string | null
  Server: string
  Status: BotStatus
}

export interface CmdSong {
  Title: string
  Source: string
  AudioType: string
  Link: string
  Length: number
  Position: number
  Paused: boolean
}

export interface CmdServerTree {
  OwnClient: number
  Server: CmdServerTreeServer
  Clients: Record<number, CmdServerTreeUser>
  Channels: Record<number, CmdServerTreeChannel>
}

export interface CmdServerTreeServer {
  Name: string
}

export interface CmdServerTreeUser {
  Id: number
  Uid: string
  Name: string
  Channel: number
}

export interface CmdServerTreeChannel {
  Id: number
  Name: string
  Parent: number
  Order: number
  HasPassword: boolean
  Subscribed: boolean
  NestedClients?: number
  Spacer?: SpacerInfo | null
}

export interface SpacerInfo {
  Type: SpacerType
  Alignment: SpacerAlignment
}

export enum SpacerType {
  None = 0,
  Solid = 1,
  Dash = 2,
  Dot = 3,
  DashDot = 4,
  DashDotDot = 5,
  Star = 6,
  // Aliases used for channel name parsing
  CSpacer = 100,
  RSpacer = 101,
  StarSpacer = 102,
}

export enum SpacerAlignment {
  Left = 0,
  Center = 1,
  Right = 2,
  Repeat = 3,
}

export interface CmdPlaylistInfo {
  Id: string
  Title: string
  SongCount: number
  DisplayOffset: number
}

export interface PlaylistItem {
  Link: string
  Title: string
  AudioType: string
}

export interface CmdPlaylist extends CmdPlaylistInfo {
  Items: PlaylistItem[]
}

export interface CmdQueueInfo extends CmdPlaylist {
  PlaybackIndex: number
}

export interface CmdRecordingInfo {
  Id: string
  Start: string
  End: string | null
  Size: number
  Duration: string | null
  IsOpen: boolean
  Participants?: CmdRecordingParticipant[]
  Waveforms?: CmdRecordingWaveformInfo[]
}

export interface CmdRecordingParticipant {
  Uid: string
  Name: string
}

export interface CmdRecordingWaveformInfo {
  Uid: string
  Name: string
  SampleRate: number
  Samples: number
  MaxSample: number
  SizeBytes: number
  FileId: string
}

export interface CmdRecordingStatus {
  Enabled: boolean
  Active: boolean
  Current: CmdRecordingInfo | null
}

export interface CmdWhisperList {
  SendMode: TargetSendMode
  GroupWhisper: {
    Target: number
    TargetId: number
    Type: number
  } | null
  WhisperClients: number[]
  WhisperChannel: number[]
}

export interface CmdVersion {
  Version: string
  Branch: string
  CommitSha: string
  BuildConfiguration: string
}

// Empty/default values
export const Empty = {
  CmdBotInfo: (): CmdBotInfo => ({
    Id: null,
    Name: null,
    Server: '',
    Status: BotStatus.Offline,
  }),

  CmdServerTreeChannel: (): CmdServerTreeChannel => ({
    Id: 0,
    Name: '',
    Order: 0,
    Parent: -1,
    HasPassword: false,
    Subscribed: false,
  }),

  CmdPlaylistInfo: (): CmdPlaylistInfo => ({
    Id: '',
    Title: '',
    SongCount: 0,
    DisplayOffset: 0,
  }),

  CmdPlaylist: (): CmdPlaylist => ({
    ...Empty.CmdPlaylistInfo(),
    Items: [],
  }),

  CmdQueueInfo: (): CmdQueueInfo => ({
    ...Empty.CmdPlaylist(),
    PlaybackIndex: 0,
  }),
}

// Bot info sync state
export interface BotInfoSync {
  botInfo: CmdBotInfo
  nowPlaying: CmdQueueInfo
  volume: number
  repeat: RepeatKind
  shuffle: boolean
  song: CmdSong | null
}

export function createBotInfoSync(): BotInfoSync {
  return {
    botInfo: Empty.CmdBotInfo(),
    nowPlaying: Empty.CmdQueueInfo(),
    volume: 0,
    repeat: RepeatKind.Off,
    shuffle: false,
    song: null,
  }
}
