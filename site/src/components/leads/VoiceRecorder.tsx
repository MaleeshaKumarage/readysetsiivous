'use client';
import { useEffect, useRef, useState } from 'react';
import { Button, Group, Paper, Stack, Text } from '@mantine/core';
import { leadCollaborationApi } from '@/lib/leadCollaborationApi';
export function VoiceRecorder({ leadId, disabled, onSaved, onDirtyChange, onBusyChange }: { leadId: string; disabled: boolean; onSaved: () => void; onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void }) {
  const recorder = useRef<MediaRecorder | null>(null); const stream = useRef<MediaStream | null>(null); const mounted = useRef(true);
  const [phase, setPhase] = useState<'idle' | 'permission' | 'recording' | 'preview' | 'saving'>('idle');
  const [draft, setDraft] = useState<{ blob: Blob; id: string } | null>(null); const [url, setUrl] = useState('');
  const [seconds, setSeconds] = useState(0); const [error, setError] = useState('');
  const stopTracks = () => { stream.current?.getTracks().forEach(track => track.stop()); stream.current = null; };
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; if (recorder.current?.state === 'recording') recorder.current.stop(); stopTracks(); }; }, []);
  useEffect(() => { onDirtyChange(phase !== 'idle'); return () => onDirtyChange(false); }, [phase, onDirtyChange]);
  useEffect(() => { onBusyChange(phase === 'saving'); return () => onBusyChange(false); }, [phase, onBusyChange]);
  useEffect(() => { if (!draft) { setUrl(''); return; } const value = URL.createObjectURL(draft.blob); setUrl(value); return () => URL.revokeObjectURL(value); }, [draft]);
  useEffect(() => { if (phase !== 'recording') return; const started = Date.now(); const timer = window.setInterval(() => { const value = Math.floor((Date.now() - started) / 1000); setSeconds(Math.min(300, value)); if (value >= 299 && recorder.current?.state === 'recording') recorder.current.stop(); }, 250); return () => clearInterval(timer); }, [phase]);
  useEffect(() => { const warn = (event: BeforeUnloadEvent) => { if (phase !== 'idle') { event.preventDefault(); event.returnValue = ''; } }; window.addEventListener('beforeunload', warn); return () => window.removeEventListener('beforeunload', warn); }, [phase]);
  const start = async () => {
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') { setError('Audio recording is not supported in this browser. Use a supported browser with microphone access.'); return; }
    const mimeType = ['audio/webm;codecs=opus', 'audio/mp4', 'audio/ogg;codecs=opus'].find(type => MediaRecorder.isTypeSupported(type));
    if (!mimeType) { setError('This browser cannot create a supported audio recording. Try Chrome or Safari.'); return; }
    setError(''); setSeconds(0); setPhase('permission');
    try {
      const source = await navigator.mediaDevices.getUserMedia({ audio: true });
      if (!mounted.current) { source.getTracks().forEach(track => track.stop()); return; }
      stream.current = source; const media = new MediaRecorder(source, { mimeType }); recorder.current = media;
      const chunks: Blob[] = []; let size = 0;
      media.ondataavailable = event => { if (event.data.size) { chunks.push(event.data); size += event.data.size; if (size > 20 * 1024 * 1024 && media.state === 'recording') media.stop(); } };
      media.onerror = () => { if (mounted.current) setError('Recording was interrupted. Preview the available recording or discard and try again.'); if (media.state === 'recording') media.stop(); stopTracks(); };
      media.onstop = () => { stopTracks(); if (!mounted.current) return; const blob = new Blob(chunks, { type: media.mimeType || mimeType }); if (!blob.size || blob.size > 20 * 1024 * 1024) { setError('The recording is empty or exceeds 20 MiB. Record a shorter note.'); setPhase('idle'); return; } setDraft({ blob, id: crypto.randomUUID() }); setPhase('preview'); };
      media.start(1000); setPhase('recording');
    } catch (failure) { stopTracks(); if (mounted.current) { setError(failure instanceof DOMException && failure.name === 'NotAllowedError' ? 'Microphone permission was denied. Allow microphone access in your browser settings and try again.' : 'The microphone could not be opened. Check the device and try again.'); setPhase('idle'); } }
  };
  const save = async () => { if (!draft) return; setPhase('saving'); setError(''); try { await leadCollaborationApi.saveRecording(leadId, draft.id, draft.blob); if (mounted.current) { setDraft(null); setPhase('idle'); onSaved(); } } catch (failure) { if (mounted.current) { setError((failure as Error).message + ' Your recording is kept here; retry saving.'); setPhase('preview'); } } };
  return <Paper withBorder p="sm"><Stack gap="xs"><Text fw={600}>Record a voice note</Text><Text size="sm">Record here, preview, then save. Maximum five minutes. Your name and recording date are added automatically.</Text>
    {error && <Text role="alert" c="red">{error}</Text>}
    {phase === 'recording' && <Text role="status">Recording {Math.floor(seconds / 60)}:{String(seconds % 60).padStart(2, '0')} / 5:00</Text>}
    {url && <audio controls src={url} aria-label="Preview voice recording" />}
    <Group>{phase === 'idle' && <Button disabled={disabled} onClick={() => void start()}>Record voice note</Button>}{phase === 'permission' && <Text role="status">Waiting for microphone permission…</Text>}{phase === 'recording' && <Button color="red" onClick={() => recorder.current?.stop()}>Stop recording</Button>}
      {(phase === 'preview' || phase === 'saving') && <><Button loading={phase === 'saving'} disabled={disabled || phase === 'saving'} onClick={() => void save()}>Save recording</Button><Button variant="default" disabled={phase === 'saving'} onClick={() => { setDraft(null); setError(''); setPhase('idle'); }}>Discard recording</Button></>}
    </Group>{disabled && <Text size="sm">Recording is disabled for closed, inactive or converted leads.</Text>}
  </Stack></Paper>;
}
