import type { Meta, StoryObj } from '@storybook/react';
import { Eyebrow, Heading } from './Heading';

const meta: Meta<typeof Heading> = {
  title: 'DS/Heading',
  component: Heading,
  parameters: {
    layout: 'centered',
    docs: {
      description: {
        component:
          'Brand headings — extrabold Inter with tight tracking, mirroring the site’s `.heading-xl/lg/md` classes. Pair with `<Eyebrow>` for the small gold kicker with the logo’s gold dot.',
      },
    },
  },
  argTypes: {
    as: { control: 'select', options: ['h1', 'h2', 'h3', 'h4'] },
    size: { control: 'select', options: ['xl', 'lg', 'md', 'sm'] },
    children: { control: 'text' },
  },
  args: {
    children: 'Siivous, joka näkyy',
    size: 'lg',
    as: 'h2',
  },
};

export default meta;
type Story = StoryObj<typeof meta>;

export const Large: Story = {};

export const Scale: Story = {
  parameters: { layout: 'padded' },
  render: (args) => (
    <div className="space-y-4 max-w-2xl">
      <Heading size="xl">Koti kuntoon, helposti</Heading>
      <Heading size="lg">Koti kuntoon, helposti</Heading>
      <Heading size="md">Koti kuntoon, helposti</Heading>
      <Heading size="sm">Koti kuntoon, helposti</Heading>
    </div>
  ),
};

export const WithEyebrow: Story = {
  render: (args) => (
    <div className="space-y-3 text-left">
      <Eyebrow>Valitse palvelu</Eyebrow>
      <Heading {...args} />
    </div>
  ),
};

export const EyebrowVariants: Story = {
  render: () => (
    <div className="space-y-3">
      <Eyebrow>Kullanpisteellä</Eyebrow>
      <Eyebrow dot={false}>Ilman pistettä</Eyebrow>
    </div>
  ),
};
