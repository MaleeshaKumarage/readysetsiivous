import type { Meta, StoryObj } from '@storybook/react';
import { Card } from './Card';
import { Heading } from './Heading';
import { Badge } from './Badge';
import { Button } from './Button';

const meta: Meta<typeof Card> = {
  title: 'DS/Card',
  component: Card,
  parameters: {
    layout: 'centered',
    docs: {
      description: {
        component:
          'Rounded content surface. `default` is the site’s workhorse card; `elevated` floats on a brand-tinted shadow; `navy` is an ink card matching the logo background.',
      },
    },
  },
  argTypes: {
    variant: { control: 'select', options: ['default', 'elevated', 'navy'] },
    padding: { control: 'select', options: ['none', 'sm', 'md', 'lg'] },
    hoverable: { control: 'boolean' },
  },
  args: {
    variant: 'default',
    padding: 'md',
    hoverable: false,
  },
};

export default meta;
type Story = StoryObj<typeof meta>;

/** A typical service card composition. */
const ServiceCard = ({ variant = 'default' as const, hoverable = false }) => (
  <Card variant={variant} hoverable={hoverable} className="w-80">
    <div className="flex items-center justify-between mb-3">
      <Badge variant="subtle">Kotisiivous</Badge>
      <span className="text-2xl">🧹</span>
    </div>
    <Heading as="h3" size="sm">
      Säännöllinen kotisiivous
    </Heading>
    <p className="mt-2 text-sm text-gray-600 dark:text-gray-300 leading-relaxed">
      Viikoittainen siivous alk. 89 € — räätälöity kotisi ja toiveidesi mukaan.
    </p>
    <div className="mt-4">
      <Button variant="gold" size="sm">
        Lue lisää
      </Button>
    </div>
  </Card>
);

export const Default: Story = {
  render: (args) => <ServiceCard {...args} />,
};

export const Elevated: Story = {
  args: { variant: 'elevated' },
  render: (args) => <ServiceCard {...args} />,
};

export const NavyInk: Story = {
  args: { variant: 'navy' },
  render: (args) => (
    <Card variant="navy" padding="lg" className="w-80">
      <span className="block w-10 h-1.5 rounded-full bg-brand-400 mb-4" aria-hidden="true" />
      <Heading as="h3" size="sm" className="text-white">
        Satakunta siivousta
      </Heading>
      <p className="mt-2 text-sm text-accent-200 leading-relaxed">
        Paikallinen siivousyritys — luotettavaa palvelua jo vuodesta 2021.
      </p>
    </Card>
  ),
};

export const PaddingScale: Story = {
  parameters: { layout: 'padded' },
  render: (args) => (
    <div className="flex flex-wrap items-start gap-6">
      {(['none', 'sm', 'md', 'lg'] as const).map((padding) => (
        <Card key={padding} padding={padding} className="w-40">
          <span className="text-sm font-semibold text-gray-700 dark:text-gray-200">padding: {padding}</span>
        </Card>
      ))}
    </div>
  ),
};
