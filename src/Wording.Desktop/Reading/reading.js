'use strict';
const host = message => window.chrome.webview.postMessage({connection, ...message});
let peer, sender, channel, stream, recorder, recordUrl, timer, connection = 0, run = 0;
let active = false, finishing = false;
let disconnectTimer;
const playback = document.getElementById('playback');
function clearPlayback() {
  playback.pause(); playback.removeAttribute('src'); playback.load();
  if (recordUrl) URL.revokeObjectURL(recordUrl);
  recordUrl = null;
}
async function stopCapture() {
  clearTimeout(timer);
  if (recorder && recorder.state !== 'inactive') recorder.stop();
  // Stop the physical microphone immediately, even if the network is unavailable.
  stream?.getTracks().forEach(track => track.stop()); stream = null;
  if (sender) await sender.replaceTrack(null);
  active = false;
}
function close() {
  ++run; clearTimeout(timer); clearTimeout(disconnectTimer); disconnectTimer = null;
  stream?.getTracks().forEach(track => track.stop()); stream = null;
  if (recorder && recorder.state !== 'inactive') recorder.stop();
  peer?.close(); peer = null; sender = null; channel = null;
  active = false; finishing = false; clearPlayback();
}
function fail(message) { host({type:'error',message}); close(); }
async function warm(id) {
  close(); connection = id;
  const attempt = connection;
  try {
    const current = new RTCPeerConnection(); peer = current;
    // Negotiate audio without asking for or acquiring a microphone.
    sender = current.addTransceiver('audio', {direction:'sendrecv'}).sender;
    channel = current.createDataChannel('oai-events');
    channel.onopen = () => { if (connection === attempt) { clearTimeout(timer); host({type:'connected'}); } };
    channel.onclose = () => { if (peer === current) fail('語音資料連線已關閉。'); };
    // Assistant audio is deliberately never attached to a speaker element.
    peer.onconnectionstatechange = () => {
      if (connection !== attempt || peer !== current) return;
      if (current.connectionState === 'connected') {
        clearTimeout(disconnectTimer); disconnectTimer = null; host({type:'connectionRecovered'});
      } else if (current.connectionState === 'disconnected') {
        // A transient WebRTC interruption can recover without throwing away the warm session.
        host({type:'connectionInterrupted'});
        if (!disconnectTimer) disconnectTimer = setTimeout(() => {
          disconnectTimer = null;
          if (peer === current && current.connectionState !== 'connected') fail('語音連線中斷超過 15 秒。');
        }, 15000);
      } else if (['failed','closed'].includes(current.connectionState)) fail('語音連線已中斷。');
    };
    const offer = await current.createOffer(); await current.setLocalDescription(offer);
    if (connection !== attempt || peer !== current) return;
    host({type:'offer',sdp:current.localDescription.sdp});
    timer = setTimeout(() => fail('語音連線逾時，請按重新連線。'), 40000);
  } catch (error) { if (connection === attempt) fail(`無法建立語音連線：${error.message}`); }
}
async function record(id) {
  if (active || finishing) return;
  if (!sender || channel?.readyState !== 'open') { fail('語音連線已失效，正在重新準備。'); return; }
  run = id; const attempt = run; clearPlayback();
  try {
    const microphone = await navigator.mediaDevices.getUserMedia({audio:{echoCancellation:true,noiseSuppression:true},video:false});
    if (run !== attempt || !sender) { microphone.getTracks().forEach(track => track.stop()); return; }
    stream = microphone;
    await sender.replaceTrack(stream.getAudioTracks()[0]);
    if (run !== attempt) return;
    const chunks = [], capture = new MediaRecorder(stream); recorder = capture;
    capture.ondataavailable = event => { if (event.data.size) chunks.push(event.data); };
    capture.onstop = () => {
      if (run !== attempt || !chunks.length) return;
      recordUrl = URL.createObjectURL(new Blob(chunks, {type:capture.mimeType})); playback.src = recordUrl;
    };
    capture.start(); active = true;
    timer = setTimeout(finish, 30000); host({type:'recording',run});
  } catch (error) {
    if (run === attempt) fail(error.name === 'NotAllowedError' ? '麥克風未獲允許，請檢查 Windows 麥克風權限後重新連線。' : `無法開始朗讀：${error.message}`);
  }
}
async function finish() {
  if (!active || finishing) return;
  finishing = true;
  try { await stopCapture(); host({type:'finish',run}); }
  catch (error) { fail(`無法結束錄音：${error.message}`); }
}
async function cancel(id) {
  run = id; finishing = true;
  try { await stopCapture(); clearPlayback(); finishing = false; host({type:'cancel',run}); }
  catch (error) { fail(`無法取消錄音：${error.message}`); }
}
window.chrome.webview.addEventListener('message', async ({data}) => {
  if (data.type === 'close') { close(); return; }
  if (data.type === 'warm') { await warm(data.connection); return; }
  if (data.type === 'answer' && data.connection === connection) {
    try { if (peer) await peer.setRemoteDescription({type:'answer',sdp:data.sdp}); }
    catch (error) { fail(`無法建立音訊連線：${error.message}`); }
  } else if (data.type === 'record') await record(data.run);
  else if (data.type === 'finish' && data.run === run) await finish();
  else if (data.type === 'cancel') await cancel(data.run);
  else if (data.type === 'reset') { run = data.run; clearPlayback(); }
  else if (data.type === 'finished' && data.run === run) finishing = false;
  else if (data.type === 'replay') playback.play().catch(() => host({type:'playbackError',run}));
});
window.addEventListener('beforeunload', close);
host({type:'ready'});
