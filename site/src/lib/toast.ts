import { notifications } from '@mantine/notifications';

// One place for error toasts, so every failed action is shown the same way.
export function showError(message: string, title = 'Something went wrong') {
  notifications.show({ color: 'red', title, message, autoClose: 7000, withBorder: true });
}

export function showSuccess(message: string, title = 'Done') {
  notifications.show({ color: 'green', title, message, autoClose: 4000, withBorder: true });
}
