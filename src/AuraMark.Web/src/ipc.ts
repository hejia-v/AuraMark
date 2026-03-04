export type WebMessagePayload = {
  type: 'Init' | 'Update' | 'Command' | 'Ack' | 'Error';
  content: string;
  timestamp: number;
};

declare global {
  interface Window { chrome?: any; }
}

export const sendToHost = (payload: WebMessagePayload) => {
  const wv = window.chrome?.webview;
  if (wv?.postMessage) wv.postMessage(JSON.stringify(payload));
};

export const onHostMessage = (handler: (payload: WebMessagePayload) => void) => {
  const wv = window.chrome?.webview;
  if (!wv?.addEventListener) return;
  wv.addEventListener('message', (ev: MessageEvent) => {
    try {
      handler(JSON.parse(ev.data) as WebMessagePayload);
    } catch {
      // ignore
    }
  });
};
