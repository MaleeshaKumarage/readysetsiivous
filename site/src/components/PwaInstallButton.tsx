'use client';

import { useEffect, useState } from 'react';
import { Button, Modal, Text, Stack, Group, Tooltip, Badge } from '@mantine/core';
import { Download, Smartphone, Check, Share } from 'lucide-react';

interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>;
}

interface PwaInstallButtonProps {
  variant?: 'subtle' | 'outline' | 'filled' | 'light';
  size?: 'xs' | 'sm' | 'md';
  compact?: boolean;
}

export function PwaInstallButton({
  variant = 'light',
  size = 'sm',
  compact = false,
}: PwaInstallButtonProps) {
  const [deferredPrompt, setDeferredPrompt] = useState<BeforeInstallPromptEvent | null>(null);
  const [isStandalone, setIsStandalone] = useState(false);
  const [isIos, setIsIos] = useState(false);
  const [iosModalOpen, setIosModalOpen] = useState(false);
  const [installed, setInstalled] = useState(false);

  useEffect(() => {
    // Register service worker
    if ('serviceWorker' in navigator) {
      navigator.serviceWorker.register('/sw.js').catch((err) => {
        console.warn('SW registration failed:', err);
      });
    }

    // Check if already in standalone mode
    const checkStandalone = () => {
      const isStandaloneMode =
        window.matchMedia('(display-mode: standalone)').matches ||
        (navigator as unknown as { standalone?: boolean }).standalone === true;
      setIsStandalone(isStandaloneMode);
    };

    checkStandalone();

    // Check if iOS
    const ua = window.navigator.userAgent;
    const isIosDevice = /iphone|ipad|ipod/i.test(ua);
    setIsIos(isIosDevice);

    const handleBeforeInstallPrompt = (e: Event) => {
      e.preventDefault();
      setDeferredPrompt(e as BeforeInstallPromptEvent);
    };

    const handleAppInstalled = () => {
      setInstalled(true);
      setDeferredPrompt(null);
    };

    window.addEventListener('beforeinstallprompt', handleBeforeInstallPrompt);
    window.addEventListener('appinstalled', handleAppInstalled);

    return () => {
      window.removeEventListener('beforeinstallprompt', handleBeforeInstallPrompt);
      window.removeEventListener('appinstalled', handleAppInstalled);
    };
  }, []);

  const handleInstallClick = async () => {
    if (deferredPrompt) {
      deferredPrompt.prompt();
      const choiceResult = await deferredPrompt.userChoice;
      if (choiceResult.outcome === 'accepted') {
        setInstalled(true);
      }
      setDeferredPrompt(null);
    } else if (isIos) {
      setIosModalOpen(true);
    }
  };

  if (isStandalone || installed) {
    return (
      <Badge color="green" variant="light" size={size} leftSection={<Check size={12} />}>
        {compact ? 'Installed' : 'App Installed'}
      </Badge>
    );
  }

  // Show install button if beforeinstallprompt fired, OR if on iOS (with instructions),
  // OR as a manual trigger fallback.
  const canShowButton = deferredPrompt !== null || isIos || true;

  if (!canShowButton) {
    return null;
  }

  return (
    <>
      <Tooltip label="Install ReadySetSiivous Admin as App" withArrow>
        <Button
          variant={variant}
          color="indigo"
          size={size}
          leftSection={deferredPrompt ? <Download size={16} /> : <Smartphone size={16} />}
          onClick={handleInstallClick}
        >
          {compact ? 'Install' : 'Install App'}
        </Button>
      </Tooltip>

      <Modal
        opened={iosModalOpen}
        onClose={() => setIosModalOpen(false)}
        title="Install Admin App on iOS"
        centered
        radius="md"
      >
        <Stack gap="md">
          <Text size="sm">
            To install <b>ReadySetSiivous Admin</b> on your iPhone or iPad:
          </Text>
          <Stack gap="xs">
            <Group gap="xs" align="center">
              <Share size={18} color="#4f46e5" />
              <Text size="sm">1. Tap the <b>Share</b> button in Safari's navigation bar.</Text>
            </Group>
            <Group gap="xs" align="center">
              <Smartphone size={18} color="#4f46e5" />
              <Text size="sm">2. Scroll down and tap <b>Add to Home Screen</b>.</Text>
            </Group>
            <Group gap="xs" align="center">
              <Check size={18} color="#16a34a" />
              <Text size="sm">3. Tap <b>Add</b> in the top right corner.</Text>
            </Group>
          </Stack>
          <Button fullWidth onClick={() => setIosModalOpen(false)}>
            Got it
          </Button>
        </Stack>
      </Modal>
    </>
  );
}
