import type { Meta, StoryObj } from '@storybook/react';
import SignaturePad from './SignaturePad';

const meta: Meta<typeof SignaturePad> = {
  title: 'Components/SignaturePad',
  component: SignaturePad,
};
export default meta;

type Story = StoryObj<typeof SignaturePad>;

export const Draw: Story = {};
