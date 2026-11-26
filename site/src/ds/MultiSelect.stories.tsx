import { useState } from 'react';
import type { Meta, StoryObj } from '@storybook/react';
import { MultiSelect, type MultiSelectOption } from './MultiSelect';

const people: MultiSelectOption[] = [
  { value: 'aiden', label: 'Aiden' },
  { value: 'amelia', label: 'Amelia' },
  { value: 'neil', label: 'Neil' },
  { value: 'charles', label: 'Charles' },
];

const meta: Meta<typeof MultiSelect> = {
  title: 'DS/MultiSelect',
  component: MultiSelect,
  parameters: {
    layout: 'centered',
    docs: {
      description: {
        component: 'Dropdown list for picking one or several options. `multiple={false}` picks exactly one and closes after a pick.',
      },
    },
  },
};

export default meta;
type Story = StoryObj<typeof meta>;

function Demo({ multiple, initial }: { multiple: boolean; initial: string[] }) {
  const [value, setValue] = useState(initial);
  return <div style={{ width: 260 }}>
    <MultiSelect label={multiple ? 'Employees' : 'Employee'} options={people} value={value} onChange={setValue}
      multiple={multiple} placeholder="Choose employees" />
  </div>;
}

export const Multiple: Story = { render: () => <Demo multiple initial={['aiden', 'neil']} /> };

export const Single: Story = { render: () => <Demo multiple={false} initial={['neil']} /> };
