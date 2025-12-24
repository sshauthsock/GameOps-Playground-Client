mergeInto(LibraryManager.library, {
    // WebSocket 연결 생성 및 열기
    // 반환값: WebSocket ID (문자열 포인터)
    WebSocket_Connect: function (urlPtr, onOpenPtr, onMessagePtr, onErrorPtr, onClosePtr) {
        var url = UTF8ToString(urlPtr);
        var ws = new WebSocket(url);
        
        // WebSocket 인스턴스를 전역 객체에 저장
        if (!window._unityWebSockets) {
            window._unityWebSockets = {};
        }
        var wsId = Date.now().toString() + Math.random().toString(36).substr(2, 9);
        window._unityWebSockets[wsId] = ws;
        
        // 콜백 함수 포인터 저장
        ws._onOpenPtr = onOpenPtr;
        ws._onMessagePtr = onMessagePtr;
        ws._onErrorPtr = onErrorPtr;
        ws._onClosePtr = onClosePtr;
        ws._wsId = wsId;
        
        // WebSocket 이벤트 핸들러
        ws.onopen = function() {
            if (ws._onOpenPtr) {
                try {
                    if (typeof Module !== 'undefined' && Module.dynCall) {
                        Module.dynCall('v', ws._onOpenPtr);
                    } else if (typeof dynCall !== 'undefined') {
                        dynCall('v', ws._onOpenPtr);
                    } else if (typeof Runtime !== 'undefined' && Runtime.dynCall) {
                        Runtime.dynCall('v', ws._onOpenPtr);
                    }
                } catch (e) {
                    console.error('WebSocket onopen callback error:', e);
                }
            }
        };
        
        ws.onmessage = function(event) {
            console.log('[WebSocket.jslib] onmessage 호출됨. data type:', typeof event.data, 'instanceof ArrayBuffer:', event.data instanceof ArrayBuffer);
            
            if (ws._onMessagePtr && event.data instanceof ArrayBuffer) {
                try {
                    var data = new Uint8Array(event.data);
                    console.log('[WebSocket.jslib] ArrayBuffer 데이터 수신:', data.length, '바이트');
                    var dataPtr = _malloc(data.length);
                    HEAP8.set(data, dataPtr);
                    console.log('[WebSocket.jslib] C# 콜백 호출 시도. onMessagePtr:', ws._onMessagePtr);
                    if (typeof Module !== 'undefined' && Module.dynCall) {
                        Module.dynCall('vii', ws._onMessagePtr, [dataPtr, data.length]);
                        console.log('[WebSocket.jslib] Module.dynCall 호출 완료');
                    } else if (typeof dynCall !== 'undefined') {
                        dynCall('vii', ws._onMessagePtr, [dataPtr, data.length]);
                        console.log('[WebSocket.jslib] dynCall 호출 완료');
                    } else if (typeof Runtime !== 'undefined' && Runtime.dynCall) {
                        Runtime.dynCall('vii', ws._onMessagePtr, [dataPtr, data.length]);
                        console.log('[WebSocket.jslib] Runtime.dynCall 호출 완료');
                    } else {
                        console.error('[WebSocket.jslib] dynCall 함수를 찾을 수 없음!');
                    }
                    _free(dataPtr);
                } catch (e) {
                    console.error('[WebSocket.jslib] onmessage callback error:', e);
                    if (dataPtr) _free(dataPtr);
                }
            } else {
                console.warn('[WebSocket.jslib] onMessagePtr가 없거나 ArrayBuffer가 아님. onMessagePtr:', ws._onMessagePtr, 'data type:', typeof event.data);
            }
        };
        
        ws.onerror = function(error) {
            if (ws._onErrorPtr) {
                try {
                    if (typeof Module !== 'undefined' && Module.dynCall) {
                        Module.dynCall('v', ws._onErrorPtr);
                    } else if (typeof dynCall !== 'undefined') {
                        dynCall('v', ws._onErrorPtr);
                    } else if (typeof Runtime !== 'undefined' && Runtime.dynCall) {
                        Runtime.dynCall('v', ws._onErrorPtr);
                    }
                } catch (e) {
                    console.error('WebSocket onerror callback error:', e);
                }
            }
        };
        
        ws.onclose = function(event) {
            if (ws._onClosePtr) {
                try {
                    if (typeof Module !== 'undefined' && Module.dynCall) {
                        Module.dynCall('vi', ws._onClosePtr, [event.code]);
                    } else if (typeof dynCall !== 'undefined') {
                        dynCall('vi', ws._onClosePtr, [event.code]);
                    } else if (typeof Runtime !== 'undefined' && Runtime.dynCall) {
                        Runtime.dynCall('vi', ws._onClosePtr, [event.code]);
                    }
                } catch (e) {
                    console.error('WebSocket onclose callback error:', e);
                }
            }
        };
        
        // ID를 문자열로 반환하기 위한 버퍼 할당
        var idPtr = _malloc(wsId.length + 1);
        stringToUTF8(wsId, idPtr, wsId.length + 1);
        
        return idPtr;
    },
    
    // WebSocket 메시지 전송
    WebSocket_Send: function (wsIdPtr, dataPtr, dataLength) {
        var wsId = UTF8ToString(wsIdPtr);
        var ws = window._unityWebSockets ? window._unityWebSockets[wsId] : null;
        
        if (!ws || ws.readyState !== WebSocket.OPEN) {
            return 0;
        }
        
        // HEAP8.buffer의 전체 버퍼를 보내지 않도록 데이터를 복사
        // new Uint8Array(HEAP8.buffer, dataPtr, dataLength)의 .buffer는 전체 HEAP8.buffer를 참조함
        var data = new Uint8Array(dataLength);
        for (var i = 0; i < dataLength; i++) {
            data[i] = HEAP8[dataPtr + i];
        }
        ws.send(data.buffer);
        return 1;
    },
    
    // WebSocket 연결 상태 확인
    WebSocket_GetReadyState: function (wsIdPtr) {
        var wsId = UTF8ToString(wsIdPtr);
        var ws = window._unityWebSockets ? window._unityWebSockets[wsId] : null;
        
        if (!ws) return 3; // CLOSED
        return ws.readyState; // 0: CONNECTING, 1: OPEN, 2: CLOSING, 3: CLOSED
    },
    
    // WebSocket 연결 종료
    WebSocket_Close: function (wsIdPtr) {
        var wsId = UTF8ToString(wsIdPtr);
        var ws = window._unityWebSockets ? window._unityWebSockets[wsId] : null;
        
        if (!ws) return 0;
        
        ws.close();
        if (window._unityWebSockets) {
            delete window._unityWebSockets[wsId];
        }
        return 1;
    },
    
    // 문자열 메모리 해제
    WebSocket_FreeString: function (ptr) {
        if (ptr) {
            _free(ptr);
        }
    }
});

