import { API_URL } from './api';

export type CefrLevel = 'A1' | 'A2' | 'B1' | 'B2' | 'C1' | 'C2';

export const CEFR_LEVELS: CefrLevel[] = ['A1', 'A2', 'B1', 'B2', 'C1', 'C2'];

export interface CreateGroupRequest {
  name: string;
  geminiApiKey: string;
  description: string;
  creatorName: string;
  creatorLevel: CefrLevel;
}

export interface CreateGroupResult {
  groupId: string;
  joinKey: string;
  creatorPlayerId: string;
}

export async function createEntertainmentGroup(
  fields: CreateGroupRequest
): Promise<CreateGroupResult> {
  const response = await fetch(`${API_URL}/api/v1/entertainment/groups`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(fields),
  });
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.title || `Create group failed (${response.status})`);
  }
  return (await response.json()) as CreateGroupResult;
}

export interface JoinGroupResult {
  groupId: string;
  playerId: string;
  playerName: string;
  level: string;
}

export async function joinEntertainmentGroup(
  joinKey: string,
  playerName: string,
  level: CefrLevel
): Promise<JoinGroupResult> {
  const response = await fetch(`${API_URL}/api/v1/entertainment/groups/${joinKey}/join`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ joinKey, playerName, level }),
  });
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.title || `Join group failed (${response.status})`);
  }
  return (await response.json()) as JoinGroupResult;
}

export interface LobbyPlayer {
  playerId: string;
  name: string;
  level: string;
  isCreator: boolean;
  isOnline: boolean;
}

export interface Lobby {
  groupId: string;
  groupName: string;
  status: string;
  players: LobbyPlayer[];
}

export async function getEntertainmentLobby(groupId: string): Promise<Lobby> {
  const response = await fetch(`${API_URL}/api/v1/entertainment/groups/${groupId}/lobby`);
  if (!response.ok) {
    throw new Error(`Lobby load failed (${response.status})`);
  }
  return (await response.json()) as Lobby;
}

export interface RoundStarted {
  roundNumber: number;
  translatorPlayerId: string;
  sentenceEn: string;
  sentenceFi: string | null;
  baseForms: string[];
}

export interface ScoreEntry {
  playerId: string;
  score: number;
}

export interface Standing {
  playerId: string;
  name: string;
  score: number;
  isCreator: boolean;
}

// sessionStorage keys for reconnect across a refresh.
export const ENT_SESSION_KEYS = {
  groupId: 'entGroupId',
  playerId: 'entPlayerId',
} as const;

export interface EntertainmentSession {
  groupId: string;
  playerId: string;
  joinKey?: string;
  isCreator?: boolean;
}

const SESSION_STORAGE_KEY = 'entSession';

export function saveEntertainmentSession(session: EntertainmentSession): void {
  try {
    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
  } catch {
    // storage unavailable (private mode) — reconnect just won't persist.
  }
}

export function loadEntertainmentSession(): EntertainmentSession | null {
  try {
    const raw = localStorage.getItem(SESSION_STORAGE_KEY);
    return raw ? (JSON.parse(raw) as EntertainmentSession) : null;
  } catch {
    return null;
  }
}

export function clearEntertainmentSession(): void {
  try {
    localStorage.removeItem(SESSION_STORAGE_KEY);
  } catch {
    // ignore
  }
}
