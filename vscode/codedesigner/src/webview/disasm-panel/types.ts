export interface EmuOption {
  label: string;
  value: string;
}

export interface SnapshotEntry {
  label: string;
  timestamp: string;
  path: string;
  name: string;
  date: string;
  isoName: string;
}

export interface DiscElfEntry {
  Path: string;
  Name: string;
  Size: number;
  IsBootElf: boolean;
}
