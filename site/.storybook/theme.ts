import { create } from '@storybook/theming';

/**
 * Storybook UI chrome themed to the ReadySetSiivous brand —
 * navy ink #0A1741 and gold #DDC067 straight from the logo.
 */
export const brandTheme = create({
  base: 'light',

  // Gold (logo accent) drives interactive chrome; navy is the secondary.
  colorPrimary: '#c9a340',
  colorSecondary: '#0d1428',

  appBg: '#fbf7ec',
  appContentBg: '#ffffff',
  appPreviewBg: '#ffffff',
  appBorderColor: '#ebdba0',
  appBorderRadius: 12,

  textColor: '#0d1428',
  textInverseColor: '#fbf7ec',
  textMutedColor: '#67501d',

  barBg: '#fbf7ec',
  barTextColor: '#67501d',
  barSelectedColor: '#c9a340',
  barHoverColor: '#9c7c26',

  inputBg: '#ffffff',
  inputBorder: '#ebdba0',
  inputTextColor: '#0d1428',
  inputBorderRadius: 8,

  brandTitle: 'ReadySetSiivous DS',
  brandUrl: 'https://readysetsiivous.fi/',
  brandTarget: '_self',
  brandImage: '/logo.png',

  fontBase: "'Inter', system-ui, -apple-system, sans-serif",
  fontCode: "ui-monospace, 'Cascadia Code', Consolas, monospace",
});
