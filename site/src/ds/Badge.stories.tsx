import type { Meta, StoryObj } from '@storybook/react';
import { Badge } from './Badge';

const meta: Meta<typeof Badge> = {
  title: 'DS/Badge',
  component: Badge,
  parameters: {
    layout: 'centered',
    docs: {
      description: {
        component:
          'Small pill label. `subtle` is the soft gold wash for inline use on white surfaces; `gold`/`navy`/`green` are solid, `outline` is the hairline variant.',
      },
    },
  },
  argTypes: {
    variant: {
      control: 'select',
      options: ['gold', 'navy', 'green', 'outline', 'subtle'],
    },
    size: { control: 'select', options: ['sm', 'md'] },
    children: { control: 'text' },
  },
  args: {
    children: 'Kotitalousvähennys',
    variant: 'subtle',
    size: 'sm',
  },
};

export default meta;
type Story = StoryObj<typeof meta>;

export const Subtle: Story = {};

export const Gold: Story = { args: { variant: 'gold' } };

export const Navy: Story = { args: { variant: 'navy' } };

export const Green: Story = { args: { variant: 'green', children: 'Ekopesu' } };

export const Outline: Story = { args: { variant: 'outline' } };

export const Sizes: Story = {
  render: (args) => (
    <div className="flex items-center gap-3">
      <Badge {...args} size="sm">
        Small
      </Badge>
      <Badge {...args} size="md">
        Medium
      </Badge>
    </div>
  ),
};
