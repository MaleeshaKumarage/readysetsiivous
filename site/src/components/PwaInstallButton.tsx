'use client';

import { useEffect, useState } from 'react';
import { Button, Modal, Text, Stack, Group, Tooltip, Badge } from '@mantine/core';
import { Download, Smartphone, Check, Share } from 'lucide-react';
import { useLanguage } from '@/hooks/useLanguage';
import type { Language } from '@/i18n';

interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>;
}

interface PwaInstallButtonProps {
  variant?: 'subtle' | 'outline' | 'filled' | 'light';
  size?: 'xs' | 'sm' | 'md';
  compact?: boolean;
  language?: Language;
}

export function PwaInstallButton({
  variant = 'light',
  size = 'sm',
  compact = false,
  language,
}: PwaInstallButtonProps) {
  const { lang: contextLang } = useLanguage();
  const [siteLang, setSiteLang] = useState<Language>(contextLang);
  const lang = language ?? siteLang;

  useEffect(() => {
    // An explicit workspace language takes precedence from the first render.
    if (language) return;
    // Callers without an override retain the saved site/context language.
    try {
      const saved = localStorage.getItem('lang');
      setSiteLang(saved === 'en' || saved === 'fi' || saved === 'sv' ? saved : contextLang);
    } catch {
      setSiteLang(contextLang);
    }
  }, [contextLang, language]);
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

  const copy = {
    fi: {
      install: 'Asenna', installApp: 'Asenna sovellus', installed: 'Asennettu', appInstalled: 'Sovellus asennettu',
      title: 'Asenna Admin-sovellus iOS-laitteelle',
      introduction: 'Näin asennat ReadySetSiivous Admin -sovelluksen iPhoneen tai iPadiin:',
      share: '1. Napauta Safarin navigointipalkin Jaa-painiketta.',
      home: '2. Vieritä alas ja napauta Lisää Koti-valikkoon.',
      add: '3. Napauta oikeassa yläkulmassa Lisää.',
      done: 'Selvä', close: 'Sulje iOS-asennusohjeet',
    },
    sv: {
      install: 'Installera', installApp: 'Installera app', installed: 'Installerad', appInstalled: 'Appen är installerad',
      title: 'Installera Admin-appen på iOS',
      introduction: 'Så installerar du ReadySetSiivous Admin på din iPhone eller iPad:',
      share: '1. Tryck på Dela i Safaris navigeringsfält.',
      home: '2. Rulla ned och tryck på Lägg till på hemskärmen.',
      add: '3. Tryck på Lägg till uppe till höger.',
      done: 'Jag förstår', close: 'Stäng installationsanvisningarna för iOS',
    },
    en: {
      install: 'Install', installApp: 'Install App', installed: 'Installed', appInstalled: 'App Installed',
      title: 'Install Admin App on iOS',
      introduction: 'To install ReadySetSiivous Admin on your iPhone or iPad:',
      share: "1. Tap the Share button in Safari's navigation bar.",
      home: '2. Scroll down and tap Add to Home Screen.',
      add: '3. Tap Add in the top right corner.',
      done: 'Got it', close: 'Close iOS installation instructions',
    },
  }[lang];

  if (isStandalone || installed) {
    return (
      <Badge color="green" variant="light" size={size} leftSection={<Check size={12} />}>
        {compact ? copy.installed : copy.appInstalled}
      </Badge>
    );
  }

  // Show install button if beforeinstallprompt fired, OR if on iOS (with instructions),
  // OR as a manual trigger fallback.
  const canShowButton = deferredPrompt !== null || isIos || true;

  if (!canShowButton) {
    return null;
  }

  const tooltipLabel =
    lang === 'fi'
      ? 'Asenna ReadySetSiivous Sovelluksena'
      : lang === 'sv'
      ? 'Installera ReadySetSiivous som app'
      : 'Install ReadySetSiivous as App';

  const ariaLabel =
    lang === 'fi'
      ? 'Asenna ReadySetSiivous-sovellus laitteellesi'
      : lang === 'sv'
      ? 'Installera ReadySetSiivous-appen på din enhet'
      : 'Install ReadySetSiivous app on your device';

  return (
    <>
      <Tooltip label={tooltipLabel} withArrow>
        <Button
          variant={variant}
          color="indigo"
          size={size}
          leftSection={deferredPrompt ? <Download size={16} /> : <Smartphone size={16} />}
          onClick={handleInstallClick}
          aria-label={ariaLabel}
        >
          {compact ? copy.install : copy.installApp}
        </Button>
      </Tooltip>

      <Modal
        opened={iosModalOpen}
        onClose={() => setIosModalOpen(false)}
        title={copy.title}
        closeButtonProps={{ 'aria-label': copy.close }}
        centered
        radius="md"
      >
        <Stack gap="md">
          <Text size="sm">
            {copy.introduction}
          </Text>
          <Stack gap="xs">
            <Group gap="xs" align="center">
              <Share size={18} color="#4f46e5" />
              <Text size="sm">{copy.share}</Text>
            </Group>
            <Group gap="xs" align="center">
              <Smartphone size={18} color="#4f46e5" />
              <Text size="sm">{copy.home}</Text>
            </Group>
            <Group gap="xs" align="center">
              <Check size={18} color="#16a34a" />
              <Text size="sm">{copy.add}</Text>
            </Group>
          </Stack>
          <Button fullWidth onClick={() => setIosModalOpen(false)}>
            {copy.done}
          </Button>
        </Stack>
      </Modal>
    </>
  );
}
