# DSCC

Dance Station Control Center(DSCC)는 Orbbec Femto Bolt/Mega 기반의 댄스 스테이션 운영용 WPF 앱입니다. 카메라 연결, Station 매핑, Depth/IR/Color 프리뷰, Skeleton 추적 상태 확인, ROI/발 마커 캘리브레이션, Unity 송신을 한 화면에서 관리합니다.

## 현재 범위

- Orbbec Femto Bolt/Mega 장치 검색 및 Station 자동 할당
- Orbbec SDK v2 기반 Depth/IR/Color 라이브 프리뷰
- Azure Kinect K4A wrapper 기반 body tracking
- Depth 프리뷰 위 Skeleton joint/bone 오버레이
- Station별 ROI, 발 마커, Unity anchor 설정
- `StationSkeletonFrame` UDP MessagePack 송신
- Unity 이벤트 UDP JSON 송신
- 전면 카메라용 skeleton X mirror 토글
- Head/Neck rotation 안정화 필터
- Replay/test frame 송신
- WPF 운영 UI, 로그, 진단 패널

## 프로젝트 구조

```text
DSCC.sln
DSCC.Live.x64.cmd
config/
  wall-a.example.json
  wall-a.local.json
spec/
src/
  DSCC.App.Wpf/        WPF 운영 앱
  DSCC.Core/           Station, ROI, calibration, config domain
  DSCC.Orbbec/         Orbbec SDK, K4A body tracking adapter
  DSCC.Protocol/       Unity로 보내는 DTO와 transform/filter
  DSCC.Replay/         fake/replay skeleton frame source
  DSCC.Transport/      UDP MessagePack/JSON sender
  OrbbecSDK.CSharp/    Orbbec C# wrapper project
tests/
third_party/orbbec/    Orbbec SDK runtime and wrapper
```

## 요구사항

- Windows x64
- .NET 10 SDK
- Git LFS
- Orbbec Femto Bolt 또는 Femto Mega
- USB 3.x 연결 권장
- Unity 수신 앱 또는 Unity Editor 쪽 DSCC receiver

이 저장소는 Orbbec 런타임 DLL/EXE/LIB/BIN 파일을 Git LFS로 관리합니다. 새 PC에서 clone한 뒤에는 반드시 LFS 파일을 받아야 합니다.

```powershell
git lfs install
git clone https://github.com/0dot77/DSCC.git
cd DSCC
git lfs pull
```

## 실행

가장 쉬운 방법은 루트의 실행 스크립트를 쓰는 것입니다.

```powershell
.\DSCC.Live.x64.cmd
```

스크립트는 WPF 앱을 x64로 빌드한 뒤 아래 exe를 실행합니다.

```text
src\DSCC.App.Wpf\bin\x64\Debug\net10.0-windows\DSCC.App.Wpf.exe
```

수동 빌드/실행은 다음 명령을 사용합니다.

```powershell
dotnet build .\src\DSCC.App.Wpf\DSCC.App.Wpf.csproj -p:Platform=x64
.\src\DSCC.App.Wpf\bin\x64\Debug\net10.0-windows\DSCC.App.Wpf.exe
```

## 기본 운영 순서

1. Orbbec 카메라를 USB 3.x 포트에 연결합니다.
2. `.\DSCC.Live.x64.cmd`로 DSCC를 실행합니다.
3. `Refresh Orbbec devices`로 장치를 검색합니다.
4. `Auto assign`으로 검색된 장치를 Station에 매핑합니다.
5. 프리뷰 모드를 `Depth` 또는 `Infrared`로 둡니다.
6. `Start Orbbec live`를 누릅니다.
7. Depth 프리뷰와 skeleton overlay로 사람이 제대로 잡히는지 확인합니다.
8. Calibration 패널에서 발 마커 중심, ROI, Unity anchor를 조정합니다.
9. `전체 설정 저장` 또는 `Save config`로 저장합니다.
10. Unity Editor에서 Play를 시작하고 DSCC가 skeleton frame을 송신하는지 확인합니다.

## 프리뷰 모드

- `Depth`: 실제 추적 확인에 가장 유용합니다. Depth image와 Skeleton overlay를 함께 봅니다.
- `Infrared`: IR stream 상태 확인용입니다.
- `Color`: 컬러 프리뷰 확인용입니다. 현재 body tracking은 Depth/K4A capture 경로에 맞춰져 있어 Color 모드에서는 skeleton tracking이 비활성화될 수 있습니다.

프레임이 낮거나 사람이 가까운 거리에서 사라지면 먼저 USB 연결이 3.x인지, `config/wall-a.local.json`의 `depthMode`와 ROI Z 범위가 현장 거리와 맞는지 확인합니다.

## 설정 파일

기본 설정은 `config/wall-a.local.json`을 사용합니다. 예시 파일은 `config/wall-a.example.json`입니다.

주요 항목:

```json
{
  "unity": {
    "host": "127.0.0.1",
    "skeletonPort": 55010,
    "eventPort": 55011,
    "statusPort": 55012,
    "mirrorSkeletonX": true,
    "stabilizeHeadRotation": true
  },
  "stations": [
    {
      "stationId": 1,
      "device": {
        "deviceType": "FemtoBolt",
        "serial": "",
        "depthMode": "WFOV_2X2BINNED",
        "fps": 15
      }
    }
  ]
}
```

현장에서 자주 조정하는 값:

- `device.serial`: Station에 고정할 Orbbec serial
- `device.depthMode`: Depth FOV/해상도 모드
- `device.fps`: 카메라 FPS
- `calibration.footMarkerCenter`: 발 마커 중심
- `calibration.trackingRoi`: 사람을 추적할 공간 범위
- `calibration.unityAnchor`: Unity 공간으로 보낼 기준 위치/회전
- `unity.mirrorSkeletonX`: 전면 카메라 기준 좌우 반전
- `unity.stabilizeHeadRotation`: Head/Neck rotation 튐 완화

## Unity 연동

DSCC는 Unity로 두 종류의 UDP 메시지를 보냅니다.

```text
Skeleton stream: UDP MessagePack :55010
Event stream:    UDP JSON        :55011
Status receive:  UDP             :55012
```

Unity가 주로 받아야 하는 데이터는 `DSCC.Protocol.StationSkeletonFrame`입니다. DTO는 `src/DSCC.Protocol`에 있고, Unity 쪽 세부 구현 메모는 `spec/Unity_Custom_Editor_Handoff.md`를 참고합니다.

전면에서 사람을 바라보는 카메라 구조에서는 `Mirror skeleton X for front-facing camera`를 켜야 Unity 캐릭터의 좌우 손이 자연스럽게 맞습니다. Head/Neck rotation이 튀면 `Stabilize head and neck rotation`을 켜고 smoothing, max deg/sec, min confidence, deadzone 값을 조정합니다.

## 테스트

전체 테스트:

```powershell
dotnet test .\DSCC.sln -p:Platform=x64
```

주요 테스트 프로젝트:

```powershell
dotnet test .\tests\DSCC.Core.Tests\DSCC.Core.Tests.csproj -p:Platform=x64
dotnet test .\tests\DSCC.Orbbec.Tests\DSCC.Orbbec.Tests.csproj -p:Platform=x64
dotnet test .\tests\DSCC.Protocol.Tests\DSCC.Protocol.Tests.csproj -p:Platform=x64
```

## 문제 해결

### 앱이 더블클릭으로 켜지지 않음

`DSCC.App.Wpf.exe`를 직접 실행하기 전에 먼저 x64 빌드가 필요합니다. 루트에서 `.\DSCC.Live.x64.cmd`를 실행하면 빌드와 실행을 같이 처리합니다.

### K4A body tracking unavailable

K4A body tracking 빌드 플래그, NuGet body tracking runtime, Orbbec K4A wrapper DLL 경로를 확인합니다. 기본 빌드는 `EnableK4aBodyTracking=true`입니다.

```powershell
dotnet build .\src\DSCC.App.Wpf\DSCC.App.Wpf.csproj -p:Platform=x64
```

### Timed out waiting for capture to be enqueued

카메라 stream 모드와 body tracking 입력 경로가 맞지 않을 때 발생할 수 있습니다. 먼저 `Depth` 프리뷰 모드로 전환하고 다시 `Start Orbbec live`를 실행합니다.

### 사람이 조금만 뒤로 가도 Depth에서 사라짐

다음을 순서대로 확인합니다.

1. USB 3.x 포트 연결 여부
2. `depthMode`가 현장 범위에 맞는지
3. `trackingRoi.minZ/maxZ`가 실제 거리 범위를 포함하는지
4. 카메라 각도와 사람의 위치가 FOV 안에 들어오는지
5. Depth 프리뷰의 valid/min/max depth 로그

### Unity에서 왼손/오른손이 반대로 움직임

Unity Link 패널에서 `Mirror skeleton X for front-facing camera`를 켭니다. 설정 저장 후 live/replay를 다시 시작하면 mirror가 송신 프레임에 적용됩니다.

## 개발 참고 문서

- `spec/DSCC_Development_Handoff.md`
- `spec/Unity_Custom_Editor_Handoff.md`
- `spec/design/dscc_wpf_mockup.html`
