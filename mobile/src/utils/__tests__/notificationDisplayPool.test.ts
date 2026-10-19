import { NotificationEvent } from '../../types/notification';
import { notificationIcon } from '../notificationDisplay';

describe('notificationIcon - task returned to the pool', () => {
  it('uses the same quiet "person removed" icon as an unassignment', () => {
    expect(notificationIcon(NotificationEvent.TaskReturnedToPool)).toEqual(notificationIcon(NotificationEvent.TaskUnassigned));
  });
});
