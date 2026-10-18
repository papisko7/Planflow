import { useState } from 'react';
import { AccordionSection } from './AccordionSection';
import { ChangePasswordCard } from './ChangePasswordCard';
import { PinSettingsCard } from './PinSettingsCard';

// "Credentials": everything that proves who you are - PIN login (set, change, turn off) and the account password.
export function CredentialsSection() {
  const [pinEnabled, setPinEnabled] = useState(false);

  return (
    <AccordionSection testID="credentials-section" title="Credentials" summary={pinEnabled ? 'PIN login on' : 'PIN login off'}>
      <PinSettingsCard onEnabledChange={setPinEnabled} />
      <ChangePasswordCard />
    </AccordionSection>
  );
}
