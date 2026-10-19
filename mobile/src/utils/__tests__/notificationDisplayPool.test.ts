import { NotificationEvent } from '../../types/notification';
import { notificationIcon } from '../notificationDisplay';

describe('notificationIcon - task returned to the pool (7.5.10b)', () => {
  it('uses the same quiet "person removed" icon as an unassignment', () => {
    expect(notificationIcon(NotificationEvent.TaskReturnedToPool)).toEqual(notificationIcon(NotificationEvent.TaskUnassigned));
  });
});
