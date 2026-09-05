import { addons } from '@storybook/manager-api';
import { brandTheme } from './theme';

addons.setConfig({
  theme: brandTheme,
  sidebar: {
    showRoots: true,
    collapsedRoots: ['foundations'],
  },
});
