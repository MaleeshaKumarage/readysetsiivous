import { API_URL } from './api';

export type CefrLevel = 'A1' | 'A2' | 'B1' | 'B2' | 'C1' | 'C2';

export const CEFR_LEVELS: CefrLevel[] = ['A1', 'A2', 'B1', 'B2', 'C1', 'C2'];

export interface CreateGroupRequest {
  name: string;
  description: string;
  creatorName: string;
  creatorLevel: CefrLevel;
}

export interface CreateGroupResult {
  groupId: string;
  joinKey: string;
  creatorPlayerId: string;
  playerToken: string;
}

type ApiProblem = { title?: string; detail?: string; errors?: Record<string, string[]> };

function problemMessage(body: ApiProblem | null, fallback: string): string {
  const validationMessages = Object.values(body?.errors ?? {}).flat()
    .filter((message): message is string => typeof message === 'string' && message.trim().length > 0);
  if (validationMessages.length > 0) return [...new Set(validationMessages)].join(' ');
  return body?.detail || body?.title || fallback;
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
    const body = await response.json().catch(() => null) as ApiProblem | null;
    throw new Error(problemMessage(body, `Create group failed (${response.status})`));
  }
  return (await response.json()) as CreateGroupResult;
}

export interface JoinGroupResult {
  groupId: string;
  playerId: string;
  playerName: string;
  playerToken: string;
  level: string;
}

export async function joinEntertainmentGroup(
  joinKey: string,
  playerName: string,
  level: CefrLevel
): Promise<JoinGroupResult> {
  const response = await fetch(`${API_URL}/api/v1/entertainment/groups/${encodeURIComponent(joinKey)}/join`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ joinKey, playerName, level }),
  });
  if (!response.ok) {
    const body = await response.json().catch(() => null) as ApiProblem | null;
    throw new Error(problemMessage(body, `Join group failed (${response.status})`));
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
  roundId: string;
  roundNumber: number;
  translatorPlayerId: string;
  sentenceEn: string;
  sentenceFi: string | null;
  baseForms: string[];
  hints: { english: string; finnish: string }[];
  submittedAnswer?: string | null;
  evaluation?: AnswerEvaluation | null;
}

export interface AnswerEvaluation {
  isCorrect: boolean;
  meaningPoints: number;
  finnishPoints: number;
  scorePoints: number;
  correctedAnswer: string;
  explanation: string;
  corrections: { original: string; replacement: string; reason: string }[];
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
  playerToken: string;
  joinKey?: string;
  isCreator?: boolean;
}

const SESSION_STORAGE_KEY = 'entSession';

export function saveEntertainmentSession(session: EntertainmentSession): void {
  try {
    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
  } catch {
    // storage unavailable (private mode) â€” reconnect just won't persist.
  }
}

export function loadEntertainmentSession(): EntertainmentSession | null {
  try {
    const raw = localStorage.getItem(SESSION_STORAGE_KEY);
    if (!raw) return null;
    const session = JSON.parse(raw);
    if (!session || ['groupId', 'playerId', 'playerToken'].some(key => typeof session[key] !== 'string' || !session[key].trim())) {
      clearEntertainmentSession();
      return null;
    }
    return session as EntertainmentSession;
  } catch {
    clearEntertainmentSession();
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
