import {
  HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from '@microsoft/signalr';
import { API_URL } from './api';

export type EntertainmentHubClient = HubConnection;

export function createEntertainmentHub(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(`${API_URL}/hubs/entertainment`, { withCredentials: false })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}
