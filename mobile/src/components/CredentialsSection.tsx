import { useState } from 'react';
import { AccordionSection } from './AccordionSection';
import { ChangePasswordCard } from './ChangePasswordCard';
import { PinSettingsCard } from './PinSettingsCard';

export function CredentialsSection() {
  const [pinEnabled, setPinEnabled] = useState(false);

  return (
    <AccordionSection testID="credentials-section" title="Credentials" summary={pinEnabled ? 'PIN login on' : 'PIN login off'}>
      <PinSettingsCard onEnabledChange={setPinEnabled} />
      <ChangePasswordCard />
    </AccordionSection>
  );
}
