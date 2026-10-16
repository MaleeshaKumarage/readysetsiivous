'use client';
import { useEffect, useRef, useState } from 'react';
import { Card } from '@/ds/Card';
import { Button } from '@/ds/Button';
import { Input, Textarea, Field } from '@/ds/Input';
import { CEFR_LEVELS, CefrLevel, EntertainmentSession, Lobby, RoundStarted, AnswerEvaluation, Standing, createEntertainmentGroup, joinEntertainmentGroup, getEntertainmentLobby, loadEntertainmentSession, saveEntertainmentSession, clearEntertainmentSession } from '@/lib/entertainmentApi';
import { createEntertainmentHub } from '@/lib/entertainmentHub';
import type { HubConnection } from '@microsoft/signalr';

type View = 'landing' | 'create' | 'join' | 'lobby' | 'game' | 'results';
export default function EntertainmentClient() {
  const [session, setSession] = useState<EntertainmentSession | null>(() => loadEntertainmentSession());
  const [view, setView] = useState<View>(() => loadEntertainmentSession() ? 'lobby' : 'landing');
  const [lobby, setLobby] = useState<Lobby | null>(null);
  const [round, setRound] = useState<RoundStarted | null>(null);
  const [answer, setAnswer] = useState('');
  const [evaluation, setEvaluation] = useState<AnswerEvaluation | null>(null);
  const [hints, setHints] = useState<Record<number, string>>({});
  const [revealedAnswer, setRevealedAnswer] = useState<string | null>(null);
  const [scores, setScores] = useState<Record<string, number>>({});
  const [standings, setStandings] = useState<Standing[]>([]);
  const [connected, setConnected] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const connectionRef = useRef<HubConnection | null>(null);
  const roundIdRef = useRef<string | null>(null);
  const busyRef = useRef(false);
  const pendingRef = useRef<{ method: string; args: unknown[] } | null>(null);
  useEffect(() => {
    if (!session) return;
    let cancelled = false;
    const connection = createEntertainmentHub(); connectionRef.current = connection;
    getEntertainmentLobby(session.groupId).then(l => { if (!cancelled) setLobby(l); }).catch(() => {});
    connection.on('lobbyUpdated', (l: Lobby) => setLobby(l));
    connection.on('roundStarted', (r: RoundStarted) => {
      const sameRound = roundIdRef.current === r.roundId;
      setAnswer(previous => r.submittedAnswer ?? (sameRound ? previous : ''));
      setHints(previous => sameRound ? previous : {});
      setRevealedAnswer(r.sentenceFi); setEvaluation(r.evaluation ?? null); setRound(r); roundIdRef.current = r.roundId;
      setBusy(false); busyRef.current = false; pendingRef.current = null; setError(null); setView('game');
    });
    connection.on('hintRevealed', (h: { roundId: string; index: number; finnish: string }) => {
      if (h.roundId === roundIdRef.current) setHints(previous => ({ ...previous, [h.index]: h.finnish }));
    });
    connection.on('answerRevealed', (a: { roundId: string; sentenceFi: string }) => {
      if (a.roundId === roundIdRef.current) setRevealedAnswer(a.sentenceFi);
    });
    connection.on('answerEvaluated', (a: { roundId: string; answer: string; evaluation: AnswerEvaluation }) => {
      if (a.roundId !== roundIdRef.current) return;
      setAnswer(a.answer); setEvaluation(a.evaluation); setBusy(false); busyRef.current = false; pendingRef.current = null; setError(null);
    });
    connection.on('scoreUpdated', (entries: { playerId: string; score: number }[]) => setScores(Object.fromEntries(entries.map(e => [e.playerId, e.score]))));
    connection.on('gameEnded', (s: Standing[]) => { setStandings(s); setView('results'); setBusy(false); busyRef.current = false; pendingRef.current = null; });
    connection.on('gameError', (message: string) => { setBusy(false); busyRef.current = false; setError(message || 'Request failed. Please retry.'); });
    const join = async () => {
      setConnected(false);
      let timeout: ReturnType<typeof setTimeout> | undefined;
      try { await Promise.race([connection.invoke('JoinLobby', session.groupId, session.playerId, session.playerToken), new Promise((_, reject) => { timeout = setTimeout(() => reject(new Error('Rejoin timed out')), 15000); })]); } finally { clearTimeout(timeout); }
      if (!cancelled) { setConnected(true); setError(null); }
    };
    const disconnected = () => { setConnected(false); setBusy(false); busyRef.current = false; setError('Connection lost. Please reconnect.'); };
    connection.onreconnecting(disconnected); connection.onclose(disconnected);
    connection.onreconnected(() => { void join().catch(() => setError('Rejoin failed. Please reconnect.')); });
    let connectTimeout: ReturnType<typeof setTimeout> | undefined;
    Promise.race([connection.start(), new Promise((_, reject) => { connectTimeout = setTimeout(() => reject(new Error('Connection timed out')), 15000); })]).then(join).finally(() => clearTimeout(connectTimeout)).catch(() => { if (!cancelled) setError('Connection failed. Please reconnect.'); });
    return () => { cancelled = true; connectionRef.current = null; void connection.stop().catch(() => {}); };
  }, [session]);
  async function send(method: string, ...args: unknown[]) {
    if (!connected || busyRef.current || !connectionRef.current) return;
    busyRef.current = true; setBusy(true); setError(null); pendingRef.current = { method, args };
    let timeout: ReturnType<typeof setTimeout> | undefined;
    try { await Promise.race([connectionRef.current.invoke(method, ...args), new Promise((_, reject) => { timeout = setTimeout(() => reject(new Error('Timeout')), 70000); })]); }
    catch { setError('Request failed. Please retry.'); }
    finally { clearTimeout(timeout); setBusy(false); busyRef.current = false; }
  }
  async function leave() {
    if (session && connected) {
      try { await Promise.race([connectionRef.current?.invoke('LeaveLobby', session.groupId, session.playerId), new Promise(resolve => setTimeout(resolve, 3000))]); } catch {}
    }
    clearEntertainmentSession(); setSession(null); setLobby(null); setRound(null); roundIdRef.current = null; setError(null); setView('landing');
  }
  function adopt(s: EntertainmentSession) { saveEntertainmentSession(s); setSession(s); setView('lobby'); }
  const translator = round?.translatorPlayerId === session?.playerId;
  const disabled = !connected || busy;
  return <main className="mx-auto max-w-2xl px-4 py-10 space-y-6">
    <h1 className="text-3xl font-bold">Language Game</h1><p>Create a group, share the join key, and play a realtime translation game together.</p>
    {view === 'landing' && <div className="flex gap-4"><Button onClick={() => setView('create')}>Create group</Button><Button onClick={() => setView('join')}>Join group</Button></div>}
    {(view === 'create' || view === 'join') && <GroupForm create={view === 'create'} onBack={() => setView('landing')} onSession={adopt} />}
    {session && <p role="status">{connected ? 'Connected' : 'Connecting…'}</p>}
    {error && <div role="alert">{error}{connected && pendingRef.current && <Button onClick={() => { const action = pendingRef.current; if (action) void send(action.method, ...action.args); }}>Retry</Button>}
      {!connected && <Button onClick={() => setSession(session ? { ...session } : null)}>Reconnect</Button>}</div>}
    {busy && <p role="status">Working…</p>}
    {view === 'lobby' && session && <Card padding="lg" className="space-y-4"><h2>{lobby?.groupName || 'Lobby'}</h2>
      {session.joinKey && <><p className="text-3xl">{session.joinKey}</p><Button onClick={() => void navigator.clipboard.writeText(session.joinKey!)}>Copy key</Button>
        <Button onClick={() => void navigator.clipboard.writeText(`${window.location.origin}/entertainment/?join=${encodeURIComponent(session.joinKey!)}`)}>Copy join link</Button></>}
      {!lobby?.players.length && <p>Waiting for players to join...</p>}
      <ul>{lobby?.players.map(p => <li key={p.playerId}>{p.name} {p.level} {p.isCreator && <span>Host</span>}</li>)}</ul>
      {session.isCreator && <Button disabled={disabled} onClick={() => void send('StartGame', session.groupId, session.playerId)}>Start game</Button>}
      <Button onClick={() => void leave()}>Leave</Button></Card>}
    {view === 'game' && session && round && <Card padding="lg" className="space-y-5">
      <h2 className="text-xl">Round {round.roundNumber}</h2><p>{translator ? 'Translate this into Finnish' : 'Someone is translating — you have the answer'}</p>
      <div>{lobby?.players.map(p => <span className="mr-4" key={p.playerId}>{p.name} <b>{scores[p.playerId] ?? 0}</b></span>)}</div>
      <p data-testid="sentence-en" className="text-2xl">{round.sentenceEn}</p>
      {!translator && (revealedAnswer ? <p data-testid="sentence-fi-revealed">{revealedAnswer}</p> : <Button data-testid="sentence-fi" disabled={disabled} onClick={() => void send('RevealAnswer', session.groupId, session.playerId, round.roundId)}>Reveal Finnish answer</Button>)}
      <table className="w-full text-left"><caption>Hint words</caption><thead><tr><th>English</th><th>Finnish</th></tr></thead><tbody>{round.hints.map((hint, index) => <tr key={index}><td>{hint.english}</td><td>{hints[index] !== undefined ? <span data-testid="revealed-word">{hints[index]}</span> : <button type="button" data-testid="covered-word" aria-label={`Reveal Finnish for ${hint.english}`} disabled={disabled || !!evaluation} onClick={() => void send('RevealHint', session.groupId, session.playerId, index, round.roundId)}>Reveal</button>}</td></tr>)}</tbody></table>
      {translator && <><Field label="Your Finnish answer"><Textarea aria-label="Your Finnish answer" value={answer} maxLength={2000} disabled={!!evaluation} onChange={e => setAnswer(e.target.value)} /></Field>
        {!evaluation && <Button disabled={disabled || !answer.trim()} onClick={() => void send('CheckAnswer', session.groupId, session.playerId, round.roundId, answer)}>Check answer</Button>}</>}
      {evaluation && <div data-testid="answer-feedback" aria-live="polite" className="space-y-3"><h3>{evaluation.isCorrect ? 'Meaning correct' : 'Meaning not fully matched'}</h3>
        <p data-testid="round-score">Score this round: {evaluation.scorePoints} / 10 ({evaluation.meaningPoints} / 7 meaning, {evaluation.finnishPoints} / 3 Finnish)</p>
        <p>Original answer: <span>{answer}</span></p><p>Corrected answer: <span>{evaluation.correctedAnswer}</span></p>
        <ul>{evaluation.corrections.map((c, index) => <li key={index}><del>{c.original}</del> → <mark>{c.replacement}</mark>: {c.reason}</li>)}</ul><p>Why: {evaluation.explanation}</p>
        {translator && <Button disabled={disabled} onClick={() => void send('NextRound', session.groupId, session.playerId, round.roundId)}>Next round</Button>}</div>}
      {session.isCreator && <Button disabled={disabled} onClick={() => void send('EndGame', session.groupId, session.playerId)}>End game</Button>}<Button onClick={() => void leave()}>Leave</Button>
    </Card>}
    {view === 'results' && <Card padding="lg"><h2>Results</h2><ol>{standings.map((s, index) => <li key={s.playerId}>{s.name} {s.score} {index === 0 && <span>Winner</span>}</li>)}</ol><Button onClick={() => void leave()}>Leave</Button></Card>}
  </main>;
}
function GroupForm({ create, onBack, onSession }: { create: boolean; onBack: () => void; onSession: (s: EntertainmentSession) => void }) {
  const [name, setName] = useState(''); const [description, setDescription] = useState(''); const [playerName, setPlayerName] = useState('');
  const [joinKey, setJoinKey] = useState(() => typeof window === 'undefined' ? '' : new URLSearchParams(window.location.search).get('join') ?? '');
  const [level, setLevel] = useState<CefrLevel | ''>(''); const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  async function submit() {
    if (!level) { setError('Choose your level.'); return; }
    if (create && (description.trim().length < 10 || description.length > 2000)) { setError('Description must contain 10 to 2000 characters.'); return; }
    setBusy(true); setError('');
    try {
      if (create) { const r = await createEntertainmentGroup({ name, description, creatorName: playerName, creatorLevel: level }); onSession({ groupId: r.groupId, playerId: r.creatorPlayerId, playerToken: r.playerToken, joinKey: r.joinKey, isCreator: true }); }
      else { const r = await joinEntertainmentGroup(joinKey.trim(), playerName, level); onSession({ groupId: r.groupId, playerId: r.playerId, playerToken: r.playerToken, isCreator: false }); }
    } catch (e) { setError(e instanceof Error ? e.message : 'Request failed.'); } finally { setBusy(false); }
  }
  return <Card padding="lg" className="space-y-5"><h2>{create ? 'Create group' : 'Join group'}</h2>
    {create ? <><Field label="Group name"><Input placeholder="e.g. Travel English" value={name} onChange={e => setName(e.target.value)} /></Field>
      <Field label="Description" hint={`${description.length} / 2000 characters (minimum 10)`} error={description.length > 0 && description.trim().length < 10 ? 'At least 10 characters required.' : undefined}><Textarea maxLength={2000} placeholder="Describe the topic or theme" value={description} onChange={e => setDescription(e.target.value)} /></Field></> :
      <Field label="Join key"><Input placeholder="6-character key" value={joinKey} onChange={e => setJoinKey(e.target.value)} /></Field>}
    <Field label="Your name"><Input placeholder="Your name" value={playerName} onChange={e => setPlayerName(e.target.value)} /></Field>
    <div><p>Your level</p><div className="flex gap-2">{CEFR_LEVELS.map(l => <button type="button" key={l} aria-pressed={level === l} onClick={() => setLevel(l)} className="border rounded p-2">{l}</button>)}</div></div>
    {error && <p role="alert">{error}</p>}<Button onClick={onBack}>Back</Button><Button disabled={busy} onClick={() => void submit()}>{busy ? 'Working…' : create ? 'Create group' : 'Join group'}</Button>
  </Card>;
}
