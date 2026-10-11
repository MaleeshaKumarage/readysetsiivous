'use client';

import { useState } from 'react';
import { Card } from '@/ds/Card';
import { Button } from '@/ds/Button';
import { Input, Textarea, Field } from '@/ds/Input';
import {
  CEFR_LEVELS,
  CefrLevel,
  createEntertainmentGroup,
  CreateGroupResult,
  ENT_SESSION_KEYS,
} from '@/lib/entertainmentApi';

type View = 'landing' | 'create' | 'join' | 'lobby';

export default function EntertainmentClient() {
  const [view, setView] = useState<View>('landing');
  const [group, setGroup] = useState<CreateGroupResult | null>(null);

  function handleCreated(result: CreateGroupResult) {
    sessionStorage.setItem(ENT_SESSION_KEYS.groupId, result.groupId);
    sessionStorage.setItem(ENT_SESSION_KEYS.playerId, result.creatorPlayerId);
    setGroup(result);
    setView('lobby');
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
        <CreateForm onBack={() => setView('landing')} onCreated={handleCreated} />
      )}

      {view === 'join' && <JoinForm onBack={() => setView('landing')} />}

      {view === 'lobby' && group && <Lobby group={group} />}
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
  onCreated: (result: CreateGroupResult) => void;
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

function JoinForm({ onBack }: { onBack: () => void }) {
  return (
    <Card variant="elevated" padding="lg" className="space-y-5">
      <h2 className="text-xl font-semibold">Join group</h2>
      <Field label="Join key">
        <Input placeholder="6-character key" />
      </Field>
      <Field label="Your name">
        <Input placeholder="Your name" />
      </Field>
      <div>
        <span className="block mb-1.5 text-sm font-semibold text-gray-700 dark:text-gray-200">Your level</span>
        <LevelSelect value="" onChange={() => {}} />
      </div>
      <p className="text-sm text-gray-500 dark:text-gray-400">
        Joining goes live in the next step.
      </p>
      <div className="flex gap-3">
        <Button variant="outline" onClick={onBack}>
          Back
        </Button>
        <Button disabled fullWidth>
          Join group
        </Button>
      </div>
    </Card>
  );
}

function Lobby({ group }: { group: CreateGroupResult }) {
  return (
    <Card variant="elevated" padding="lg" className="space-y-5 text-center">
      <h2 className="text-xl font-semibold">Group created</h2>
      <p className="text-gray-600 dark:text-gray-300">
        Share this key so others can join:
      </p>
      <div className="text-4xl font-mono tracking-[0.5em] text-accent-900 dark:text-white">
        {group.joinKey}
      </div>
      <p className="text-sm text-gray-500 dark:text-gray-400">
        Waiting for players to join...
      </p>
    </Card>
  );
}
