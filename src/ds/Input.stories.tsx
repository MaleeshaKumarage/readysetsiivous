import type { Meta, StoryObj } from '@storybook/react';
import { Field, Input, Textarea } from './Input';

const meta: Meta<typeof Input> = {
  title: 'DS/Input',
  component: Input,
  parameters: {
    layout: 'centered',
    docs: {
      description: {
        component:
          'Form controls with a gold focus ring. Use `<Field>` to wrap a control with a label, helper hint, and error state.',
      },
    },
  },
  argTypes: {
    placeholder: { control: 'text' },
    disabled: { control: 'boolean' },
  },
  args: {
    placeholder: 'Osoitteesi…',
  },
};

export default meta;
type Story = StoryObj<typeof meta>;

export const Plain: Story = {
  render: (args) => <Input {...args} className="w-72" />,
};

export const WithLabel: Story = {
  parameters: { layout: 'padded' },
  render: (args) => (
    <div className="w-80 space-y-5">
      <Field label="Nimi" hint="Etunimi ja sukunimi">
        <Input placeholder="Matti Meikäläinen" />
      </Field>
      <Field label="Sähköposti">
        <Input type="email" placeholder="matti@example.fi" />
      </Field>
      <Field label="Viesti">
        <Textarea placeholder="Kerro siivoustarpeestasi…" />
      </Field>
    </div>
  ),
};

export const WithError: Story = {
  render: (args) => (
    <div className="w-72 space-y-5">
      <Field label="Puhelinnumero" error="Anna kelvollinen puhelinnumero">
        <Input
          placeholder="040 123 4567"
          aria-invalid
          className="border-red-400 focus:border-red-500 focus:ring-red-500/20"
        />
      </Field>
      <Field label="Disabled">
        <Input defaultValue="Ei muokattavissa" disabled />
      </Field>
    </div>
  ),
};
