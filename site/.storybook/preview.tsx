import type { Preview } from '@storybook/react';
import { withThemeByClassName } from '@storybook/addon-themes';
import '../src/app/globals.css';
import { brandTheme } from './theme';

const preview: Preview = {
  parameters: {
    controls: {
      matchers: {
        color: /(background|color)$/i,
        date: /Date$/i,
      },
    },
    layout: 'centered',
    docs: {
      theme: brandTheme,
    },
    options: {
      storySort: {
        order: ['Foundations', ['Brand'], 'DS'],
      },
    },
  },
  // The site uses Tailwind's class-based dark mode (`dark:` variants),
  // so the theme switcher toggles `.dark` on <html>.
  decorators: [
    withThemeByClassName({
      themes: { light: '', dark: 'dark' },
      defaultTheme: 'light',
    }),
  ],
  tags: ['autodocs'],
};

export default preview;
