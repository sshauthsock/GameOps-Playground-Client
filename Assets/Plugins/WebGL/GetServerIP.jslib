mergeInto(LibraryManager.library, {
  GetServerIP: function () {
    // URL 파라미터에서 서버 IP 가져오기
    var urlParams = new URLSearchParams(window.location.search);
    var serverIP = urlParams.get("server") || "";

    // URL 파라미터가 없으면 기본값 사용 (나중에 설정 파일로 대체 가능)
    if (!serverIP) {
      // 기본값은 빈 문자열로 반환 (Unity에서 설정 파일 확인)
      serverIP = "";
    }

    // Unity WebGL에서 문자열을 반환하기 위한 메모리 할당
    var lengthBytes = lengthBytesUTF8(serverIP) + 1;
    var buffer = _malloc(lengthBytes);
    stringToUTF8(serverIP, buffer, lengthBytes);
    return buffer;
  },

  GetServerPort: function () {
    // URL 파라미터에서 서버 포트 가져오기
    var urlParams = new URLSearchParams(window.location.search);
    var serverPort = urlParams.get("port") || "7777";

    // 포트를 정수로 변환하여 반환
    return parseInt(serverPort, 10);
  },
});
