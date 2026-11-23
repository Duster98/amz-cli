import {
  HubConnectionBuilder,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr'

export type LibraryChangedEvent = {
  type: 'library_changed'
  revision: number
  changed_at: string
}

export type HistoryChangedEvent = {
  type: 'history_changed'
  revision: number
  changed_at: string
}

export type MonitorChangedEvent = {
  type: 'monitor_changed'
  revision: number
  changed_at: string
}

export type CompareChangedEvent = {
  type: 'compare_changed'
  revision: number
  changed_at: string
}

export type UpdateChangedEvent = {
  type: 'update_changed'
  revision: number
  phase: string
  updated_at: string
}

export type UpdateProgressEvent = {
  type: 'update_progress'
  revision: number
  phase: string
  stage: string
  message: string
  percent?: number | null
  bytes: number
  total_bytes: number
  updated_at: string
}

export function createLibraryConnection(
  onLibraryChanged: (event: LibraryChangedEvent) => void,
  onReconnected: () => void,
  onHistoryChanged?: (event: HistoryChangedEvent) => void,
  onUpdateChanged?: (event: UpdateChangedEvent) => void,
  onUpdateProgress?: (event: UpdateProgressEvent) => void,
  onMonitorChanged?: (event: MonitorChangedEvent) => void,
  onCompareChanged?: (event: CompareChangedEvent) => void,
): HubConnection {
  const connection = new HubConnectionBuilder()
    .withUrl('/api/v1/hubs/library', {
      headers: { 'X-Amz-Cli-Web': '1' },
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(LogLevel.Warning)
    .build()

  connection.on('LibraryChanged', onLibraryChanged)
  connection.on('HistoryChanged', onHistoryChanged ?? (() => {}))
  connection.on('MonitorChanged', onMonitorChanged ?? (() => {}))
  connection.on('CompareChanged', onCompareChanged ?? (() => {}))
  connection.on('UpdateChanged', onUpdateChanged ?? (() => {}))
  connection.on('UpdateProgress', onUpdateProgress ?? (() => {}))
  connection.onreconnected(onReconnected)
  return connection
}
