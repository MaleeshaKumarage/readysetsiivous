'use client';

import { useEffect, useRef, useState } from 'react';
import { Card } from '@/ds/Card';
import { Button } from '@/ds/Button';
import { Input, Textarea, Field } from '@/ds/Input';
import {
  CEFR_LEVELS,
  CefrLevel,
  createEntertainmentGroup,
  getEntertainmentLobby,
  joinEntertainmentGroup,
  Lobby,
  LobbyPlayer,
  RoundStarted,
  ScoreEntry,
  Standing,
  saveEntertainmentSession,
  loadEntertainmentSession,
  clearEntertainmentSession,
} from '@/lib/entertainmentApi';
import { createEntertainmentHub } from '@/lib/entertainmentHub';
import type { HubConnection } from '@microsoft/signalr';

type View = 'landing' | 'create' | 'join' | 'lobby' | 'game' | 'results';

interface Session {
  groupId: string;
  playerId: string;
  joinKey?: string;
  isCreator?: boolean;
}

export default function EntertainmentClient() {
  const [view, setView] = useState<View>(() => (loadEntertainmentSession() ? 'lobby' : 'landing'));
  const [session, setSession] = useState<Session | null>(() => loadEntertainmentSession());
  const [lobby, setLobby] = useState<Lobby | null>(null);
  const [round, setRound] = useState<RoundStarted | null>(null);
  const [scores, setScores] = useState<Record<string, number>>({});
  const [standings, setStandings] = useState<Standing[] | null>(null);
  const [answerRevealed, setAnswerRevealed] = useState(false);
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    if (!session) return;
    let cancelled = false;

    getEntertainmentLobby(session.groupId)
      .then((l) => { if (!cancelled) setLobby(l); })
      .catch(() => {});

    const connection = createEntertainmentHub();
    connectionRef.current = connection;

    connection.on('lobbyUpdated', (l: Lobby) => setLobby(l));
    connection.on('roundStarted', (r: RoundStarted) => {
      setRound(r);
      setAnswerRevealed(false);
      setView('game');
    });
    connection.on('answerRevealed', () => setAnswerRevealed(true));
    connection.on('scoreUpdated', (entries: ScoreEntry[]) => {
      const map: Record<string, number> = {};
      entries.forEach((e) => { map[e.playerId] = e.score; });
      setScores(map);
    });
    connection.on('gameEnded', (st: Standing[]) => {
      setStandings(st);
      setView('results');
    });

    connection
      .start()
      .then(() => connection.invoke('JoinLobby', session.groupId, session.playerId))
      .catch(() => {});

    return () => {
      cancelled = true;
      connectionRef.current = null;
      connection.stop().catch(() => {});
    };
  }, [session]);

  function send(method: string, ...args: unknown[]) {
    connectionRef.current?.invoke(method, ...args).catch(() => {});
  }

  function startGame() {
    if (!session) return;
    send('StartGame', session.groupId, session.playerId);
  }

  function leave() {
    clearEntertainmentSession();
    setSession(null);
    setLobby(null);
    setRound(null);
    setStandings(null);
    setScores({});
    setView('landing');
  }

  return (
    <main className="mx-auto max-w-2xl px-4 py-10">
      <h1 className="text-3xl font-bold text-accent-900 dark:text-white mb-2">Language Game</h1>
      <p className="text-gray-600 dark:text-gray-300 mb-8">
        Create a group, share the join key, and play a realtime translation game together.
      </p>

      {view === 'landing' && (
        <div className="grid gap-4 sm:grid-cols-2">
          <Card variant="elevated" padding="lg" className="text-center">
            <h2 className="text-xl font-semibold mb-2">Create group</h2>
            <p className="text-gray-500 dark:text-gray-400 mb-4">
              Start a new group and get a join key to share.
            </p>
            <Button onClick={() => setView('create')} fullWidth>
              Create group
            </Button>
          </Card>
          <Card variant="elevated" padding="lg" className="text-center">
            <h2 className="text-xl font-semibold mb-2">Join group</h2>
            <p className="text-gray-500 dark:text-gray-400 mb-4">
              Have a key? Join an existing group.
            </p>
            <Button variant="outline" onClick={() => setView('join')} fullWidth>
              Join group
            </Button>
          </Card>
        </div>
      )}

      {view === 'create' && (
        <CreateForm
          onBack={() => setView('landing')}
          onCreated={(result) => {
            const s: Session = {
              groupId: result.groupId,
              playerId: result.creatorPlayerId,
              joinKey: result.joinKey,
              isCreator: true,
            };
            saveEntertainmentSession(s);
            setSession(s);
            setView('lobby');
          }}
        />
      )}

      {view === 'join' && (
        <JoinForm
          onBack={() => setView('landing')}
          onJoined={(result) => {
            const s: Session = { groupId: result.groupId, playerId: result.playerId, isCreator: false };
            saveEntertainmentSession(s);
            setSession(s);
            setView('lobby');
          }}
        />
      )}

      {view === 'lobby' && session && (
        <LobbyView
          session={session}
          lobby={lobby}
          onStart={startGame}
          onLeave={leave}
        />
      )}

      {view === 'game' && session && round && (
        <GameView
          key={round.roundNumber}
          session={session}
          round={round}
          isTranslator={round.translatorPlayerId === session.playerId}
          players={lobby?.players ?? []}
          scores={scores}
          answerRevealed={answerRevealed}
          send={send}
        />
      )}

      {view === 'results' && standings && (
        <ResultsView standings={standings} onLeave={leave} />
      )}
    </main>
  );
}

function LevelSelect({ value, onChange }: { value: CefrLevel | ''; onChange: (v: CefrLevel) => void }) {
  return (
    <div className="grid grid-cols-6 gap-2">
      {CEFR_LEVELS.map((lvl) => (
        <button
          key={lvl}
          type="button"
          onClick={() => onChange(lvl)}
          className={
            'rounded-xl border px-2 py-2 text-sm font-semibold transition-colors ' +
            (value === lvl
              ? 'bg-accent-900 text-white border-accent-900'
              : 'border-gray-300 dark:border-gray-600 text-gray-700 dark:text-gray-200 hover:border-brand-500')
          }
        >
          {lvl}
        </button>
      ))}
    </div>
  );
}

function CreateForm({
  onBack,
  onCreated,
}: {
  onBack: () => void;
  onCreated: (result: { groupId: string; joinKey: string; creatorPlayerId: string }) => void;
}) {
  const [name, setName] = useState('');
  const [geminiApiKey, setGeminiApiKey] = useState('');
  const [description, setDescription] = useState('');
  const [creatorName, setCreatorName] = useState('');
  const [level, setLevel] = useState<CefrLevel | ''>('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const descriptionCount = description.length;

  async function submit() {
    if (!level) {
      setError('Choose your level.');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const result = await createEntertainmentGroup({
        name,
        geminiApiKey,
        description,
        creatorName,
        creatorLevel: level,
      });
      onCreated(result);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Create group failed.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Card variant="elevated" padding="lg" className="space-y-5">
      <h2 className="text-xl font-semibold">Create group</h2>

      <Field label="Group name">
        <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Travel English" />
      </Field>

      <Field label="Gemini API key" hint="Used to generate sentences. Stored encrypted, never shown.">
        <Input
          type="password"
          value={geminiApiKey}
          onChange={(e) => setGeminiApiKey(e.target.value)}
          placeholder="Your Gemini API key"
        />
      </Field>

      <Field
        label="Description"
        hint={`${descriptionCount} / 200 characters minimum`}
        error={descriptionCount > 0 && descriptionCount < 200 ? 'At least 200 characters required.' : undefined}
      >
        <Textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="Describe the topic or theme the sentences should be about..."
        />
      </Field>

      <Field label="Your name">
        <Input value={creatorName} onChange={(e) => setCreatorName(e.target.value)} placeholder="Your name" />
      </Field>

      <div>
        <span className="block mb-1.5 text-sm font-semibold text-gray-700 dark:text-gray-200">Your level</span>
        <LevelSelect value={level} onChange={setLevel} />
      </div>

      {error && <p className="text-sm text-red-600 dark:text-red-400">{error}</p>}

      <div className="flex gap-3">
        <Button variant="outline" onClick={onBack}>
          Back
        </Button>
        <Button onClick={submit} disabled={submitting} fullWidth>
          {submitting ? 'Creating...' : 'Create group'}
        </Button>
      </div>
    </Card>
  );
}

function JoinForm({
  onBack,
  onJoined,
}: {
  onBack: () => void;
  onJoined: (result: { groupId: string; playerId: string }) => void;
}) {
  const [joinKey, setJoinKey] = useState('');
  const [playerName, setPlayerName] = useState('');
  const [level, setLevel] = useState<CefrLevel | ''>('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function submit() {
    if (!level) {
      setError('Choose your level.');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const result = await joinEntertainmentGroup(joinKey.trim(), playerName, level);
      onJoined(result);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Join group failed.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Card variant="elevated" padding="lg" className="space-y-5">
      <h2 className="text-xl font-semibold">Join group</h2>

      <Field label="Join key">
        <Input value={joinKey} onChange={(e) => setJoinKey(e.target.value)} placeholder="6-character key" />
      </Field>

      <Field label="Your name">
        <Input value={playerName} onChange={(e) => setPlayerName(e.target.value)} placeholder="Your name" />
      </Field>

      <div>
        <span className="block mb-1.5 text-sm font-semibold text-gray-700 dark:text-gray-200">Your level</span>
        <LevelSelect value={level} onChange={setLevel} />
      </div>

      {error && <p className="text-sm text-red-600 dark:text-red-400">{error}</p>}

      <div className="flex gap-3">
        <Button variant="outline" onClick={onBack}>
          Back
        </Button>
        <Button onClick={submit} disabled={submitting} fullWidth>
          {submitting ? 'Joining...' : 'Join group'}
        </Button>
      </div>
    </Card>
  );
}

function LobbyView({
  session,
  lobby,
  onStart,
  onLeave,
}: {
  session: Session;
  lobby: Lobby | null;
  onStart: () => void;
  onLeave: () => void;
}) {
  const players = lobby?.players ?? [];

  return (
    <Card variant="elevated" padding="lg" className="space-y-5">
      <div className="text-center">
        <h2 className="text-xl font-semibold">{lobby?.groupName || 'Lobby'}</h2>
        {session.joinKey && (
          <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
            Share this key so others can join:
          </p>
        )}
        {session.joinKey && (
          <div className="mt-1 text-3xl font-mono tracking-[0.5em] text-accent-900 dark:text-white">
            {session.joinKey}
          </div>
        )}
      </div>

      <div>
        <h3 className="text-sm font-semibold text-gray-500 dark:text-gray-400 mb-2">Players</h3>
        {players.length === 0 ? (
          <p className="text-sm text-gray-500 dark:text-gray-400">Waiting for players to join...</p>
        ) : (
          <ul className="space-y-2">
            {players.map((p) => (
              <li
                key={p.playerId}
                className="flex items-center justify-between rounded-xl border border-gray-100 dark:border-gray-700 px-4 py-2"
              >
                <span className="font-medium">
                  {p.name}
                  {p.isCreator && <span className="ml-2 text-xs text-brand-600">Host</span>}
                </span>
                <span className="text-sm text-gray-500 dark:text-gray-400">{p.level}</span>
              </li>
            ))}
          </ul>
        )}
      </div>

      {session.isCreator && (
        <Button onClick={onStart} fullWidth>
          Start game
        </Button>
      )}
      <Button variant="ghost" onClick={onLeave} fullWidth>
        Leave
      </Button>
    </Card>
  );
}

function ScoreBoard({ players, scores }: { players: LobbyPlayer[]; scores: Record<string, number> }) {
  return (
    <div className="flex flex-wrap gap-2 justify-center">
      {players.map((p) => (
        <span
          key={p.playerId}
          className="inline-flex items-center gap-1 rounded-full bg-gray-100 dark:bg-gray-800 px-3 py-1 text-sm"
        >
          <span className="font-medium">{p.name}</span>
          <span className="font-bold text-accent-900 dark:text-white">{scores[p.playerId] ?? 0}</span>
        </span>
      ))}
    </div>
  );
}

function HintChip({ text, revealed, onReveal }: { text: string; revealed: boolean; onReveal: () => void }) {
  if (revealed) {
    return (
      <span
        data-testid="revealed-word"
        className="inline-flex items-center rounded-lg bg-brand-500/15 border border-brand-500/40 px-3 py-1.5 text-sm font-medium"
      >
        {text}
      </span>
    );
  }
  return (
    <button
      type="button"
      data-testid="covered-word"
      onClick={onReveal}
      className="inline-flex items-center justify-center rounded-lg border border-dashed border-gray-300 dark:border-gray-600 bg-gray-100 dark:bg-gray-800 px-3 py-1.5 text-sm text-transparent select-none hover:border-brand-500"
    >
      {text}
    </button>
  );
}

function GameView({
  session,
  round,
  isTranslator,
  players,
  scores,
  answerRevealed,
  send,
}: {
  session: Session;
  round: RoundStarted;
  isTranslator: boolean;
  players: LobbyPlayer[];
  scores: Record<string, number>;
  answerRevealed: boolean;
  send: (method: string, ...args: unknown[]) => void;
}) {
  const [revealedHints, setRevealedHints] = useState<Set<number>>(new Set());

  function revealHint(index: number) {
    setRevealedHints((prev) => new Set(prev).add(index));
    if (isTranslator) {
      send('RevealHint', session.groupId, session.playerId);
    }
  }

  function revealAnswer() {
    send('RevealAnswer', session.groupId, session.playerId);
  }

  return (
    <Card variant="elevated" padding="lg" className="space-y-6">
      <div className="text-center">
        <h2 className="text-xl font-semibold">Round {round.roundNumber}</h2>
        <p className="text-sm text-gray-500 dark:text-gray-400">
          {isTranslator ? 'Translate this into Finnish' : 'Someone is translating — you have the answer'}
        </p>
      </div>

      <ScoreBoard players={players} scores={scores} />

      <div data-testid="sentence-en" className="text-2xl font-medium text-center leading-relaxed">
        {round.sentenceEn}
      </div>

      {!isTranslator && round.sentenceFi && (
        <div className="text-center">
          <p className="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-2">
            Finnish (tap to reveal)
          </p>
          {answerRevealed ? (
            <div data-testid="sentence-fi-revealed" className="text-xl rounded-lg px-4 py-3 bg-brand-500/10">
              {round.sentenceFi}
            </div>
          ) : (
            <button
              type="button"
              data-testid="sentence-fi"
              onClick={revealAnswer}
              className="w-full text-xl text-transparent select-none bg-gray-100 dark:bg-gray-800 rounded-lg px-4 py-3 hover:bg-gray-200 dark:hover:bg-gray-700"
            >
              {round.sentenceFi}
            </button>
          )}
        </div>
      )}

      <div>
        <p className="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-2">Hint words</p>
        <div className="flex flex-wrap gap-2">
          {round.baseForms.map((word, i) => (
            <HintChip
              key={`${word}-${i}`}
              text={word}
              revealed={revealedHints.has(i)}
              onReveal={() => revealHint(i)}
            />
          ))}
        </div>
      </div>

      {isTranslator && (
        <div className="flex gap-3">
          <Button variant="outline" onClick={() => send('Skip', session.groupId, session.playerId)} fullWidth>
            Skip
          </Button>
          <Button onClick={() => send('Done', session.groupId, session.playerId)} fullWidth>
            Done
          </Button>
        </div>
      )}

      {session.isCreator && (
        <Button
          variant="outline"
          onClick={() => send('EndGame', session.groupId, session.playerId)}
          fullWidth
        >
          End game
        </Button>
      )}
    </Card>
  );
}

function ResultsView({ standings, onLeave }: { standings: Standing[]; onLeave: () => void }) {
  return (
    <Card variant="elevated" padding="lg" className="space-y-5">
      <h2 className="text-xl font-semibold text-center">Results</h2>
      <ol className="space-y-2">
        {standings.map((s, i) => (
          <li
            key={s.playerId}
            className={
              'flex items-center justify-between rounded-xl border px-4 py-3 ' +
              (i === 0 ? 'border-brand-500 bg-brand-500/10' : 'border-gray-100 dark:border-gray-700')
            }
          >
            <span className="font-medium">
              {i + 1}. {s.name}
              {i === 0 && <span className="ml-2 text-xs text-brand-600">Winner</span>}
            </span>
            <span className="font-bold text-accent-900 dark:text-white">{s.score}</span>
          </li>
        ))}
      </ol>
      <Button variant="ghost" onClick={onLeave} fullWidth>
        Leave
      </Button>
    </Card>
  );
}
