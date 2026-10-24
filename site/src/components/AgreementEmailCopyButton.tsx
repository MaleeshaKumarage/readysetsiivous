'use client';
import { useEffect, useRef, useState } from 'react';
import { Button, Stack, Text } from '@mantine/core';
import { Check, Mail } from 'lucide-react';
import { agreementEmail } from '@/lib/agreementEmail';

export function AgreementEmailCopyButton({ title, signerName, link }: { title: string; signerName: string; link: string }) {
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState('');
  const timer = useRef<ReturnType<typeof setTimeout>>();
  useEffect(() => () => clearTimeout(timer.current), []);
  const copy = async () => {
    setError(''); setCopied(false);
    try {
      if (!navigator.clipboard?.write || typeof ClipboardItem === 'undefined') throw new Error('Unsupported clipboard');
      const content = agreementEmail(title, signerName, link);
      await navigator.clipboard.write([new ClipboardItem({
        'text/html': new Blob([content.html], { type: 'text/html' }),
        'text/plain': new Blob([content.text], { type: 'text/plain' }),
      })]);
      setCopied(true); clearTimeout(timer.current);
      timer.current = setTimeout(() => setCopied(false), 2000);
    } catch {
      setError('Could not copy the formatted email. Allow clipboard access and retry in a supported browser.');
    }
  };
  const ariaLabel = copied
    ? `Copied email for ${signerName}`
    : `Copy email for ${signerName}`;

  return (
    <Stack gap={4} maw={340}>
      <Button
        size="xs"
        variant="light"
        color={copied ? 'teal' : 'gray'}
        aria-label={ariaLabel}
        leftSection={copied ? <Check size={14} /> : <Mail size={14} />}
        onClick={copy}
      >
        {copied ? 'Copied email' : 'Copy email'}
      </Button>
      <div role="status" aria-live="polite" className="sr-only">
        {copied ? `Email message for ${signerName} copied to clipboard.` : ''}
      </div>
      {error && <Text size="xs" c="red" role="alert">{error}</Text>}
    </Stack>
  );
}
