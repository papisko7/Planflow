import { Text } from 'react-native';
import { fireEvent, render, screen } from '@testing-library/react-native';
import { AccordionSection } from '../AccordionSection';

describe('AccordionSection', () => {
  it('starts collapsed: shows the title and summary, hides the content', async () => {
    await render(
      <AccordionSection testID="demo" title="Profile" summary="Ann">
        <Text>secret content</Text>
      </AccordionSection>,
    );

    expect(screen.getByText('Profile')).toBeTruthy();
    expect(screen.getByText('Ann')).toBeTruthy();
    expect(screen.queryByText('secret content')).toBeNull();
    expect(screen.getByTestId('demo-header').props.accessibilityState).toEqual({ expanded: false });
  });

  it('opens and closes on tap; the summary is only shown while collapsed', async () => {
    await render(
      <AccordionSection testID="demo" title="Profile" summary="Ann">
        <Text>secret content</Text>
      </AccordionSection>,
    );

    await fireEvent.press(screen.getByTestId('demo-header'));
    expect(screen.getByText('secret content')).toBeTruthy();
    expect(screen.queryByText('Ann')).toBeNull();
    expect(screen.getByTestId('demo-header').props.accessibilityState).toEqual({ expanded: true });

    await fireEvent.press(screen.getByTestId('demo-header'));
    expect(screen.queryByText('secret content')).toBeNull();
    expect(screen.getByText('Ann')).toBeTruthy();
  });

  it('keeps the content mounted while collapsed, so typed text is not lost', async () => {
    await render(
      <AccordionSection testID="demo" title="Form">
        <Text>draft</Text>
      </AccordionSection>,
    );

    // hidden from the user, but still in the tree
    expect(screen.getByText('draft', { includeHiddenElements: true })).toBeTruthy();
  });

  it('can start expanded', async () => {
    await render(
      <AccordionSection testID="demo" title="Open" defaultExpanded>
        <Text>visible</Text>
      </AccordionSection>,
    );

    expect(screen.getByText('visible')).toBeTruthy();
  });
});
