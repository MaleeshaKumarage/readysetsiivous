import { createTheme, Button, ActionIcon, Paper, NavLink } from '@mantine/core';

export const adminTheme = createTheme({
  primaryColor: 'indigo',
  defaultRadius: 'md',
  fontFamily: 'Inter, system-ui, sans-serif',
  headings: { fontFamily: 'Inter, system-ui, sans-serif', fontWeight: '650' },
  components: {
    Button: Button.extend({ styles: { root: { minHeight: 44 } } }),
    ActionIcon: ActionIcon.extend({ styles: { root: { minWidth: 44, minHeight: 44 } } }),
    Paper: Paper.extend({ defaultProps: { radius: 'md' } }),
    NavLink: NavLink.extend({ styles: { root: { minHeight: 44, borderRadius: 8 } } }),
  },
});
