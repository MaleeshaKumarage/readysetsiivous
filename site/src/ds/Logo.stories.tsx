import type { Meta, StoryObj } from '@storybook/react';
import { Logo } from './Logo';
import { Card } from './Card';

const meta: Meta<typeof Logo> = {
  title: 'DS/Logo',
  component: Logo,
  parameters: {
    layout: 'centered',
    docs: {
      description: {
        component:
          'The official navy/gold ReadySetSiivous logomark from `public/brand/readyset-logomark.png` plus the wordmark with its gold “Siivous” accent.',
      },
    },
  },
  argTypes: {
    size: { control: 'select', options: ['sm', 'md', 'lg'] },
    withWordmark: { control: 'boolean' },
  },
  args: {
    size: 'md',
    withWordmark: true,
  },
};

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const MarkOnly: Story = { args: { withWordmark: false } };

export const Sizes: Story = {
  parameters: { layout: 'padded' },
  render: (args) => (
    <div className="flex items-end gap-8">
      <Logo size="sm" />
      <Logo size="md" />
      <Logo size="lg" />
    </div>
  ),
};

/** The wordmark is dark-on-light by default and flips automatically with the theme. */
export const OnNavy: Story = {
  render: (args) => (
    <Card variant="navy" padding="lg" className="flex flex-col gap-4 items-start w-80">
      <Logo {...args} />
      <p className="text-sm text-accent-200">
        Mark on the logo’s own navy — wordmark flips to light.
      </p>
    </Card>
  ),
};
