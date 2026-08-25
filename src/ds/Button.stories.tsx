import type { Meta, StoryObj } from '@storybook/react';
import { Button, buttonClasses } from './Button';

const meta: Meta<typeof Button> = {
  title: 'DS/Button',
  component: Button,
  parameters: {
    layout: 'centered',
    docs: {
      description: {
        component:
          'Pill-shaped call to action. `primary` uses the logo’s deep navy, `gold` the logo’s gold with navy text, and `whatsapp` the site’s established CTA green. Use `buttonClasses(variant, size)` to wear the same styles on an `<a>`.',
      },
    },
  },
  argTypes: {
    variant: {
      control: 'select',
      options: ['primary', 'gold', 'whatsapp', 'outline', 'ghost'],
    },
    size: { control: 'select', options: ['sm', 'md', 'lg'] },
    fullWidth: { control: 'boolean' },
    disabled: { control: 'boolean' },
    children: { control: 'text' },
  },
  args: {
    children: 'Pyydä tarjous',
    size: 'md',
    variant: 'primary',
  },
};

export default meta;
type Story = StoryObj<typeof meta>;

export const Primary: Story = {};

export const Gold: Story = { args: { variant: 'gold' } };

export const WhatsApp: Story = { args: { variant: 'whatsapp', children: 'WhatsApp' } };

export const Outline: Story = { args: { variant: 'outline', children: 'Selaa palvelut' } };

export const Ghost: Story = { args: { variant: 'ghost', children: 'Peruuta' } };

export const Sizes: Story = {
  render: (args) => (
    <div className="flex items-center gap-4">
      <Button {...args} size="sm">
        Pieni
      </Button>
      <Button {...args} size="md">
        Normaali
      </Button>
      <Button {...args} size="lg">
        Iso
      </Button>
    </div>
  ),
};

export const AllVariants: Story = {
  parameters: { layout: 'padded' },
  render: (args) => (
    <div className="flex flex-wrap items-center gap-4">
      {(['primary', 'gold', 'whatsapp', 'outline', 'ghost'] as const).map((variant) => (
        <Button key={variant} {...args} variant={variant}>
          {variant}
        </Button>
      ))}
    </div>
  ),
};

export const AsLink: Story = {
  render: () => (
    <a href="#story" onClick={(e) => e.preventDefault()} className={buttonClasses('primary', 'md')}>
      Link, styled as button
    </a>
  ),
};
