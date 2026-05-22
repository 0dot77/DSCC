# Dance Station Control Center 개발 Handoff

작성일: 2026-05-21  
프로젝트 경로: `C:\Users\o77do\Developer\DSCC`  
원본 기획/기술 정리: `C:\Users\o77do\Documents\taeyang\01_Projects\봄랩\테마파크툰\Dance 기술 정리.md`

## 1. 프로젝트 정의

**Dance Station Control Center(DSCC)**는 Femto Bolt/Mega 기반 댄스 체험 시스템에서 장치 연결, 카메라 동기화, Station 매핑, 발모양 스티커 기반 ROI 캘리브레이션, skeleton 상태 판정, Unity 송신, 로그/리플레이를 담당하는 **C# WPF 운영/개발 툴**이다.

Unity 앱은 게임/연출/프로젝션을 담당하고, DSCC는 센서/트래킹/캘리브레이션 계층을 담당한다.

```text
Femto Bolt / Femto Mega
→ DSCC
  → Device 관리
  → Sync 상태 확인
  → Station 매핑
  → ROI / 발모양 스티커 캘리브레이션
  → Skeleton 추출 및 Station 상태 판정
  → Unity 송신
→ Unity Dance App
  → 게임 상태
  → 캐릭터
  → 스코어
  → 스포트라이트/이펙트
  → 프로젝터 출력
```

## 2. 현재 확정된 방향

- 테스트 장비는 **Orbbec Femto Bolt**를 사용한다.
- 현장 장비는 **Orbbec Femto Mega**를 사용한다.
- DSCC는 Femto Bolt와 Femto Mega를 모두 지원해야 한다.
- 장비별 차이는 `DeviceProfile`과 device adapter 계층으로 흡수한다.
- 현장 구조는 **카메라 1대 = Station 1개 = 유저 1명**이다.
- 현장에는 발모양 스티커를 붙이고, 관객은 해당 위치에 서서 플레이한다.
- DSCC는 카메라가 본 모든 사람을 플레이어로 인정하지 않는다.
- DSCC는 Station ROI 안에 안정적으로 들어온 skeleton만 해당 Station의 유효 플레이어로 인정한다.
- 여러 카메라가 한 사람을 합쳐 보는 multi-camera skeleton fusion은 1차 범위가 아니다.
- Unity에는 camera raw stream이 아니라 `StationId` 기준으로 정리된 skeleton/state/event만 보낸다.
- 실시간 skeleton 송신은 **UDP + MessagePack**을 1순위로 둔다.
- 이벤트/명령 송신은 **OSC 또는 UDP JSON** 중 하나를 선택한다. 기존 프로젝트 흐름상 OSC도 후보로 유지한다.
- C# 데스크톱 프레임워크는 **WPF**를 1순위로 둔다.

## 3. 1차 MVP 목표

1차 MVP는 "춤 게임 전체 제작 툴"이 아니라 **Station tracking 운영 콘솔**이다.

MVP에서 반드시 되는 것:

- Femto Bolt 1대 연결
- skeleton 수신 여부 확인
- 카메라 serial 표시
- 장비 타입 표시
- Station 1개에 카메라 매핑
- 발모양 스티커 중심점 지정
- Station ROI 설정
- skeleton이 ROI 안에 있는지 판정
- `HasPlayer`, `PlayerEntering`, `PlayerActive`, `PlayerLost`, `PlayerExited` 상태 표시
- Unity로 `StationSkeletonFrame` 송신
- skeleton frame 녹화
- 녹화 데이터 리플레이
- 장비 없이 Unity 송신 테스트

MVP에서 제외해도 되는 것:

- 전체 안무/코스 편집기
- 등급/스코어 룰 편집기
- 복잡한 프로젝션 매핑 에디터
- 여러 사람을 한 Station에서 동시에 추적
- 여러 카메라 skeleton fusion
- Mac/Linux 지원
- 상용 운영자 UI polish

## 4. 권장 기술 스택

- Language: C#
- Runtime: 최신 .NET LTS 계열
- UI: WPF
- UI pattern: MVVM
- MVVM helper: CommunityToolkit.Mvvm
- Logging: Microsoft.Extensions.Logging
- Config serialization: System.Text.Json
- Binary protocol: MessagePack-CSharp
- OSC: extOSC/SharpOSC/자체 UDP JSON 중 검토
- Chart/diagnostics: ScottPlot 또는 LiveCharts2
- 3D skeleton preview: 초기에는 WPF Canvas/2D preview로 충분, 필요 시 HelixToolkit.Wpf 검토

Unity와 공유해야 하는 protocol assembly는 가능하면 Unity 호환성을 고려해 만든다.

권장:

```text
DSCC.Protocol
- POCO DTO만 포함
- Unity에서 참조하기 쉬운 구조
- UnityEngine.Vector3 같은 Unity 타입 의존 금지
- System.Numerics.Vector3도 Unity 호환성 확인 전까지는 직접 Vector3Dto 사용
```

## 5. 권장 솔루션 구조

초기 솔루션 구조:

```text
C:\Users\o77do\Developer\DSCC
├─ DSCC.sln
├─ src
│  ├─ DSCC.App.Wpf
│  │  ├─ App.xaml
│  │  ├─ MainWindow.xaml
│  │  ├─ Views
│  │  ├─ ViewModels
│  │  └─ Resources
│  ├─ DSCC.Core
│  │  ├─ Devices
│  │  ├─ Stations
│  │  ├─ Calibration
│  │  ├─ Tracking
│  │  └─ Diagnostics
│  ├─ DSCC.Orbbec
│  │  ├─ FemtoBoltDevice.cs
│  │  ├─ FemtoMegaDevice.cs
│  │  └─ OrbbecDeviceDiscovery.cs
│  ├─ DSCC.Protocol
│  │  ├─ StationSkeletonFrame.cs
│  │  ├─ JointFrame.cs
│  │  ├─ StationState.cs
│  │  └─ DsccEvent.cs
│  ├─ DSCC.Transport
│  │  ├─ UdpMessagePackSender.cs
│  │  ├─ OscEventSender.cs
│  │  └─ UdpJsonEventSender.cs
│  └─ DSCC.Replay
│     ├─ SkeletonRecorder.cs
│     └─ SkeletonReplaySource.cs
├─ tests
│  ├─ DSCC.Core.Tests
│  └─ DSCC.Protocol.Tests
├─ config
│  ├─ wall-a.example.json
│  └─ station.example.json
└─ spec
   └─ DSCC_Development_Handoff.md
```

## 6. 핵심 도메인 모델

### Station

Station은 DSCC의 핵심 단위다. Camera 자체보다 Station을 우선해서 모델링한다.

```text
Station
- StationId
- DisplayName
- AssignedCameraSerial
- DeviceType
- FootMarkerCenter
- TrackingRoi
- UnityAnchor
- State
- LastSkeletonFrame
- Diagnostics
```

Station 상태:

```text
Empty
Entering
Active
Lost
Exited
Disabled
Error
```

상태 전이 기본안:

```text
Empty
→ Entering: skeleton이 ROI 안에서 감지됨
→ Active: 0.3~0.5초 이상 안정 감지
→ Lost: Active 이후 skeleton lost 또는 ROI 이탈
→ Exited: 1.5~3.0초 이상 lost/ROI 이탈 지속
→ Empty: 정리 완료
```

### DeviceProfile

Femto Bolt와 Femto Mega를 모두 지원해야 하므로 장비 설정은 profile로 분리한다.

```json
{
  "deviceType": "FemtoBolt",
  "serial": "BOLT_SERIAL",
  "stationId": 1,
  "connection": "USB",
  "syncRole": "Primary",
  "depthMode": "WFOV_2X2BINNED",
  "fps": 15
}
```

현장 Mega 예시:

```json
{
  "deviceType": "FemtoMega",
  "serial": "MEGA_SERIAL",
  "stationId": 1,
  "connection": "USB_OR_ETHERNET",
  "syncRole": "Primary",
  "depthMode": "WFOV_2X2BINNED",
  "fps": 15
}
```

### StationCalibration

```json
{
  "stationId": 1,
  "cameraSerial": "BOLT_OR_MEGA_SERIAL",
  "footMarkerCenter": {
    "x": 0.0,
    "y": 0.0,
    "z": 2.1
  },
  "trackingRoi": {
    "minX": -0.7,
    "maxX": 0.7,
    "minZ": 1.5,
    "maxZ": 2.8,
    "minY": 0.0,
    "maxY": 2.4
  },
  "unityAnchor": {
    "x": -3.0,
    "y": 0.0,
    "z": 0.0,
    "rotationY": 0.0
  }
}
```

## 7. Unity 송신 프로토콜

Unity가 받아야 하는 주 데이터는 `StationSkeletonFrame`이다.

초기 DTO 예시:

```csharp
public sealed class StationSkeletonFrame
{
    public int ProtocolVersion { get; set; } = 1;
    public int StationId { get; set; }
    public string CameraSerial { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public long TimestampUsec { get; set; }

    public bool HasPlayer { get; set; }
    public StationStateDto State { get; set; }
    public float Confidence { get; set; }

    public bool IsInsideFootMarker { get; set; }
    public bool IsInsideTrackingRoi { get; set; }
    public float TrackingLostSeconds { get; set; }

    public Vector3Dto PelvisLocal { get; set; } = new();
    public QuaternionDto BodyRotation { get; set; } = new();
    public JointFrameDto[] Joints { get; set; } = Array.Empty<JointFrameDto>();
}
```

Unity 타입에 의존하지 않는 DTO:

```csharp
public readonly struct Vector3Dto
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
}

public readonly struct QuaternionDto
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float W { get; init; }
}
```

Joint DTO:

```csharp
public sealed class JointFrameDto
{
    public string Name { get; set; } = "";
    public Vector3Dto PositionLocal { get; set; }
    public QuaternionDto RotationLocal { get; set; }
    public float Confidence { get; set; }
}
```

Unity 송신 원칙:

- skeleton stream은 UDP + MessagePack으로 보낸다.
- Unity는 최신 frame만 사용한다.
- 오래된 frame은 버린다.
- 누락된 UDP frame은 재전송하지 않는다.
- 이벤트/명령은 OSC 또는 UDP JSON으로 분리할 수 있다.

권장 포트 기본값:

```text
Skeleton UDP MessagePack: 55010
Event OSC/UDP JSON:       55011
Unity status receive:     55012
```

## 8. Station ROI 판정 규칙

카메라가 skeleton을 봤다고 바로 플레이어로 인정하면 안 된다. DSCC는 다음 조건을 조합해 안정 판정한다.

필수 조건:

- skeleton 골반 중심이 Station ROI 안에 있음
- 발목 또는 발 관절이 foot marker 허용 범위 안에 있음
- skeleton confidence가 threshold 이상
- 카메라와의 거리 z가 Station 허용 범위 안에 있음
- 최소 N frame 또는 0.3~0.5초 이상 연속 감지됨

권장 threshold 초기값:

```text
EnterStableSeconds: 0.4
LostGraceSeconds: 1.5
ExitConfirmSeconds: 3.0
MinSkeletonConfidence: 0.45
FootMarkerRadiusMeters: 0.45
RoiHalfWidthMeters: 0.70
RoiDepthMinMeters: 1.50
RoiDepthMaxMeters: 2.80
```

상태 전이 예:

```text
0.0s skeleton detected in ROI → Entering
0.4s stable in ROI → Active
0.2s lost → still Active
1.5s lost → Lost
3.0s lost/outside ROI → Exited
```

## 9. UI 구성

### Devices 탭

목표: 장비 연결/동기화 상태를 한눈에 본다.

필수 표시:

- 발견된 장비 목록
- device type: FemtoBolt / FemtoMega / MockReplay
- serial number
- assigned station
- connection type
- fps
- depth mode
- sync role
- timestamp
- dropped frames
- status: disconnected / connected / streaming / error

필수 액션:

- Refresh devices
- Start stream
- Stop stream
- Assign to Station
- Open device diagnostics

### Stations 탭

목표: Station별 플레이어 상태를 확인한다.

필수 표시:

- Station ID
- assigned camera serial
- state: Empty / Entering / Active / Lost / Exited
- HasPlayer
- confidence
- inside ROI
- inside foot marker
- lost seconds
- last frame time

필수 액션:

- Enable/Disable Station
- Clear Station
- Force Enter
- Force Exit
- Send test frame to Unity

### Calibration 탭

목표: 발모양 스티커 중심, ROI, Unity anchor를 저장한다.

필수 액션:

- Select Station
- Capture Foot Marker Center
- Capture Neutral Pose
- Generate ROI
- Edit ROI values manually
- Set Unity Anchor
- Send Calibration Test to Unity
- Save Calibration
- Load Calibration

초기 preview는 단순 2D top view면 충분하다.

### Unity Link 탭

목표: Unity 송신 상태를 확인하고 테스트한다.

필수 표시:

- target IP
- skeleton port
- event port
- send FPS
- last sent timestamp
- Unity heartbeat/status
- packets sent
- packet errors

필수 액션:

- Start sending
- Stop sending
- Send `game/start`
- Send `game/reset`
- Send `player/enter`
- Send `player/exit`
- Send `calibration/reload`

### Replay / Diagnostics 탭

목표: 장비 없이 재현 가능하게 만든다.

필수 기능:

- Start recording
- Stop recording
- Load recording
- Play
- Pause
- Scrub timeline
- Send replay frames to Unity
- Export logs

## 10. Config 파일

벽면별 config를 둔다.

예:

```text
config/wall-a.json
config/wall-b.json
```

예시:

```json
{
  "wallId": "wall-a",
  "unity": {
    "host": "127.0.0.1",
    "skeletonPort": 55010,
    "eventPort": 55011,
    "statusPort": 55012
  },
  "stations": [
    {
      "stationId": 1,
      "displayName": "Station 1",
      "enabled": true,
      "device": {
        "deviceType": "FemtoBolt",
        "serial": "",
        "connection": "USB",
        "syncRole": "Primary",
        "depthMode": "WFOV_2X2BINNED",
        "fps": 15
      },
      "calibration": {
        "footMarkerCenter": { "x": 0.0, "y": 0.0, "z": 2.1 },
        "trackingRoi": {
          "minX": -0.7,
          "maxX": 0.7,
          "minY": 0.0,
          "maxY": 2.4,
          "minZ": 1.5,
          "maxZ": 2.8
        },
        "unityAnchor": {
          "x": -3.0,
          "y": 0.0,
          "z": 0.0,
          "rotationY": 0.0
        }
      },
      "thresholds": {
        "enterStableSeconds": 0.4,
        "lostGraceSeconds": 1.5,
        "exitConfirmSeconds": 3.0,
        "minSkeletonConfidence": 0.45,
        "footMarkerRadiusMeters": 0.45
      }
    }
  ]
}
```

## 11. 개발 순서

### Phase 0: 솔루션 스캐폴딩

- `DSCC.sln` 생성
- project 구조 생성
- WPF 앱 실행 확인
- logging 구성
- config load/save 구성
- `DSCC.Protocol` DTO 작성
- 기본 unit test 추가

완료 조건:

- 앱이 실행된다.
- example config를 읽고 화면에 wall/station 목록을 보여준다.

### Phase 1: MockReplay 기반 Station 파이프라인

실제 카메라 없이 먼저 전체 데이터 흐름을 만든다.

- `MockReplayDevice` 작성
- 임의 skeleton frame 생성
- Station ROI 판정
- Station state machine 구현
- Unity 송신 인터페이스 작성
- UDP MessagePack sender 작성

완료 조건:

- 카메라 없이 Station 1이 Empty/Entering/Active/Lost/Exited로 전이된다.
- Unity 수신 mock 또는 console receiver에서 `StationSkeletonFrame`을 받을 수 있다.

### Phase 2: Femto Bolt 단일 장비 연동

- Orbbec SDK/K4A Wrapper 설치 절차 정리
- Femto Bolt discovery
- serial 표시
- skeleton frame 수신
- Station 1에 매핑
- ROI 판정 적용
- skeleton preview 표시

완료 조건:

- Femto Bolt 1대에서 실제 사람이 Station 1 Active 상태가 된다.
- 발모양 스티커 위치를 벗어나면 Lost/Exited가 된다.

### Phase 3: Calibration MVP

- foot marker center capture
- neutral pose capture
- ROI auto generate
- manual ROI edit
- calibration save/load
- Unity anchor edit

완료 조건:

- 앱을 재시작해도 Station 1 calibration이 유지된다.

### Phase 4: Unity Link

- UDP MessagePack stream 안정화
- event sender 선택: OSC 또는 UDP JSON
- Unity heartbeat/status 수신
- test command buttons 구현

완료 조건:

- Unity 쪽 receiver가 `StationId`, `HasPlayer`, joints를 받아 캐릭터 위치를 갱신한다.

### Phase 5: 4 Station 확장

- Station 1~4 config
- camera serial to station mapping
- 각 Station 상태 표시
- 4 device stream 상태 표시
- sync role/fps/depth mode 일치 여부 표시

완료 조건:

- 한 벽면 기준 4 Station이 독립적으로 Active/Lost/Exited 상태를 가진다.
- 옆 Station 사람이 잘못 잡히는 상황을 ROI로 줄일 수 있다.

### Phase 6: Replay / Diagnostics

- skeleton recording
- replay load/play/pause
- packet/log export
- device diagnostics export

완료 조건:

- 카메라 없이 녹화 데이터로 Unity 게임 흐름 테스트가 가능하다.

## 12. Orbbec 연동 주의사항

- Femto Bolt와 Femto Mega가 완전히 동일하게 동작한다고 가정하지 말 것.
- SDK wrapper 계층에서 device type 차이를 숨긴다.
- native DLL 로딩 경로를 명확히 관리한다.
- Unity 프로젝트 경로에는 한글/공백 경로를 피한다.
- DSCC 프로젝트 경로도 가능하면 ASCII 경로 유지: 현재 `C:\Users\o77do\Developer\DSCC`는 적합하다.
- SDK crash가 WPF 앱 전체를 죽일 수 있으므로 장기적으로 device worker process 분리도 고려한다. MVP에서는 같은 프로세스여도 된다.
- 장치 재연결 로직은 반드시 필요하다.
- 현장 PC는 USB 절전 해제, 고성능 전원 옵션, GPU driver 고정이 필요하다.

## 13. 테스트 계획

우선 작성할 unit test:

- Station ROI inside/outside 판정
- foot marker radius 판정
- Station state transition
- lost grace period
- config load/save roundtrip
- MessagePack serialize/deserialize roundtrip

수동 테스트:

- MockReplay frame이 Unity receiver로 도착하는지
- Station 1에서 사람이 서면 Active가 되는지
- 사람이 옆으로 벗어나면 Lost/Exited가 되는지
- skeleton이 1초 미만 끊겨도 즉시 Exited가 되지 않는지
- 앱 재시작 후 calibration이 유지되는지

현장 테스트:

- Femto Bolt 1대 단일 Station
- Femto Bolt 4대 또는 가능한 수량까지 multi Station
- Femto Mega 1대 호환성
- Femto Mega 4대 현장 구성
- Sync Hub 연결 상태
- 2시간 이상 연속 구동

## 14. 개발자가 바로 시작할 작업

다음 순서로 시작하면 된다.

1. `DSCC.sln` 생성
2. `src/DSCC.Protocol` class library 생성
3. `StationSkeletonFrame`, `JointFrameDto`, `Vector3Dto`, `QuaternionDto`, `StationStateDto` 작성
4. `src/DSCC.Core` class library 생성
5. `Station`, `StationStateMachine`, `TrackingRoi`, `StationCalibration` 작성
6. `src/DSCC.App.Wpf` WPF 앱 생성
7. example config를 읽어서 Station 목록 표시
8. `MockReplayDevice` 또는 fake skeleton generator 작성
9. Station state transition 화면 표시
10. UDP MessagePack sender 작성

첫 커밋/체크포인트의 완료 기준:

- 앱 실행
- Station 1 표시
- fake skeleton 위치를 바꾸면 Empty/Entering/Active/Lost/Exited 상태 변화
- `StationSkeletonFrame`이 UDP로 송신됨
- config save/load 가능

## 15. 열려 있는 결정 사항

- 이벤트 송신을 OSC로 할지 UDP JSON으로 할지
- Unity 수신부를 별도 package로 만들지, Unity 프로젝트 안에 직접 넣을지
- DTO assembly를 Unity가 직접 참조할지, Unity용 복사본을 둘지
- Body Tracking SDK를 DSCC에서 직접 쓸지, Unity Asset Store 예제 코드를 POC 참고로만 둘지
- Femto Mega 현장 연결을 USB로 고정할지 Ethernet/PoE까지 열어둘지
- Station 4개가 한 PC에서 모두 안정적으로 동작하는지
- 현장에서 RGB/depth 데이터를 저장할지, skeleton 로그만 저장할지

## 16. 용어 정리

- **DSCC**: Dance Station Control Center.
- **Station**: 한 명이 서는 체험 자리. 카메라 1대, 발모양 스티커 1개, Unity 캐릭터 1개와 매핑된다.
- **Foot Marker**: 현장 바닥에 붙이는 발모양 스티커. Station 중심점 역할을 한다.
- **ROI**: Region of Interest. 해당 Station에서 유효한 플레이어로 인정할 공간 범위.
- **Unity Anchor**: Station skeleton을 Unity 월드에 배치하기 위한 기준 위치.
- **DeviceProfile**: Femto Bolt/Mega 차이를 흡수하기 위한 장비 설정 단위.
- **StationSkeletonFrame**: Unity로 보내는 Station 기준 skeleton frame.

## 17. 2026-05-22 UI 기능화 작업 정리

현재 WPF 화면은 실시간 Orbbec 연결, Depth/IR/Color 프리뷰, Station 카드, Stage Map, 일부 설정 저장/송신 기능까지 동작한다. 다만 상단 내비게이션처럼 클릭 가능해 보이는 영역 중 일부는 아직 실제 컨트롤이 아니라 표시용 텍스트다. 다음 개발자는 이 섹션을 기준으로 "보이는 기능"을 실제 화면 전환과 명령으로 연결한다.

### 17.1 현재 클릭되지 않는 영역

`src/DSCC.App.Wpf/MainWindow.xaml` 기준:

- 상단 메뉴 `File`, `Devices`, `Calibration`, `Unity`, `Diagnostics`는 `TextBlock`이다. 아직 `Menu`, `Button`, `Command`가 아니다.
- 2차 내비게이션 `Workbench`, `Devices`, `Calibration`, `Unity Link`도 대부분 `TextBlock`/정적 `Border`다. 현재 선택 상태는 Workbench처럼 보이지만 실제 section routing은 없다.
- 본문 중앙 `TabControl`의 `Live stage`, `Skeleton frames`, `Device diagnostics`, `Replay`는 클릭 가능한 탭이다.
- 툴바 버튼 `Reload config`, `Save config`, `Refresh Orbbec devices`, `Auto assign`, `Start Orbbec live`, `Stop Orbbec`, `Send test frame`은 ViewModel command에 연결되어 있다.
- 오른쪽 inspector의 `Calibration`, `Unity Link` 관련 버튼은 일부 command에 연결되어 있으나 독립 화면으로 이동하는 내비게이션은 없다.

### 17.2 이미 연결된 명령

`MainWindowViewModel`에 이미 있는 command:

- Config: `RefreshConfigCommand`, `SaveConfigCommand`, `RevertConfigCommand`
- Device/Orbbec: `RefreshDevicesCommand`, `AutoAssignDevicesCommand`, `StartOrbbecLiveCommand`, `StopOrbbecLiveCommand`
- Replay/Test: `StartReplayCommand`, `StopReplayCommand`, `SendTestFrameCommand`
- Unity event: `SendEventCommand`
- Station editor: `ApplyStationEditorCommand`, `CaptureFootMarkerCommand`, `GenerateRoiCommand`
- Manual state: `ForceEnterCommand`, `ForceExitCommand`, `ClearStationCommand`

개발 원칙: 먼저 이 command들을 재사용하고, command가 없는 동작만 새로 추가한다.

### 17.3 우선 개발 작업

#### A. 실제 내비게이션 shell

목표: 상단/2차 내비게이션을 "보이는 탭"이 아니라 실제 화면 전환으로 만든다.

작업:

- `WorkspaceSection` enum 추가: `Workbench`, `Devices`, `Calibration`, `UnityLink`, `Diagnostics`, `Replay`
- `MainWindowViewModel.SelectedSection` 추가
- `NavigateCommand` 추가, command parameter로 section 이름을 받는다.
- 상단 `TextBlock`을 `Button` 또는 `MenuItem`으로 교체한다.
- 2차 내비게이션 `Workbench / Devices / Calibration / Unity Link`도 `Button`으로 교체한다.
- 선택된 section은 Accent underline과 foreground로 표시한다.
- section 전환 시 같은 데이터 컨텍스트를 유지하고 live stream은 끊지 않는다.

완료 기준:

- `Devices`를 누르면 device list/diagnostics 중심 화면으로 이동한다.
- `Calibration`을 누르면 선택 Station의 Stage Map, foot marker, ROI, threshold 편집 화면으로 이동한다.
- `Unity Link`를 누르면 Unity 연결/이벤트/packet 상태 화면으로 이동한다.
- `Diagnostics`를 누르면 로그, SDK 상태, FPS, USB connection, export 기능 중심 화면으로 이동한다.
- 키보드 탭 이동과 focus visual이 보인다.

#### B. File 메뉴 기능

목표: config 파일 작업을 명확하게 만든다.

작업:

- `File` 메뉴 항목: `Reload config`, `Save`, `Save as`, `Open config`, `Revert`, `Exit`
- `Open config`/`Save as`는 WPF file dialog 사용
- live stream 중 config 교체 시 확인 dialog 또는 stream stop 안내
- config path와 dirty state 표시

완료 기준:

- 다른 `config/*.json`을 열어도 Station/Device/Unity 설정이 화면에 즉시 반영된다.
- 저장 후 앱 재실행 시 같은 값이 유지된다.

#### C. Devices 화면

목표: 장비 연결 상태와 Station 매핑을 현장에서 바로 판단하게 한다.

작업:

- 발견 장비 목록을 메인 화면으로 승격
- USB connection 표시: `USB2.x`는 warn, `USB3.x`는 ok
- target FPS와 measured FPS를 나란히 표시
- depth mode, actual depth resolution, preview mode 표시
- Station assignment 변경 UI 추가
- selected device start/stop stream 버튼 추가
- SDK log 마지막 경고/에러 표시

완료 기준:

- Femto Bolt를 USB3로 연결하면 `USB3.1`과 약 `15fps`가 보인다.
- USB2.x이면 "대역폭 부족 가능" 경고가 보인다.
- Auto assign 없이도 장비를 Station에 직접 배정할 수 있다.

#### D. Calibration 화면

목표: 현장에서 foot marker, ROI, Unity anchor를 한 화면에서 조정한다.

작업:

- Stage Map을 독립 calibration 화면의 중심 컨트롤로 크게 배치
- live preview는 보조 패널로 유지
- foot marker drag/capture
- ROI drag/resize
- ROI numeric edit와 map drag 결과 동기화
- `Capture Neutral Pose` 기능 추가
- `Generate ROI`는 현재 skeleton/foot marker 기준으로 생성
- `Apply`와 `Save config`의 차이를 명확히 표시
- 입력값 validation: min/max 역전, 음수 radius, 비정상 threshold 방지

완료 기준:

- 발 마커를 움직이면 map, numeric value, saved config가 일관되게 바뀐다.
- ROI 안/밖 판정이 Stage Map과 Station 카드에 즉시 반영된다.
- 앱 재시작 후 calibration 값이 유지된다.

#### E. Unity Link 화면

목표: Unity 송신 상태와 테스트 명령을 분리된 운영 화면에서 관리한다.

작업:

- Unity host/ports 편집
- `Start sending` / `Stop sending` 명령 분리
- skeleton stream packet count, error count, last sent timestamp 표시
- Unity heartbeat/status 수신 구현
- event command 버튼: `game/start`, `game/reset`, `player/enter`, `player/exit`, `calibration/reload`
- selected station 강제 enter/exit/clear 버튼 배치

완료 기준:

- Unity receiver 없이도 packet count와 send error가 명확히 보인다.
- Unity heartbeat가 들어오면 `UnityStatus`가 갱신된다.
- event command 전송 성공/실패가 로그에 남는다.

#### F. Diagnostics 화면

목표: 현장 문제를 앱 안에서 바로 확인하고 내보낼 수 있게 한다.

작업:

- 앱 로그 필터: info/warn/error
- Orbbec SDK log tail 표시
- FPS history, dropped frames, body tracking status 표시
- 현재 SDK/native DLL 로딩 상태 표시
- export diagnostics: config, app log, Orbbec SDK log, device list를 zip으로 저장
- replay recording 목록/로드/재생/일시정지/timeline scrub

완료 기준:

- "왜 fps가 낮은지"를 USB mode, stream target, measured fps, SDK warning으로 추적할 수 있다.
- 현장 이슈 리포트용 zip을 버튼 하나로 만들 수 있다.

### 17.4 권장 구현 순서

1. `WorkspaceSection`과 `NavigateCommand`를 추가한다.
2. 상단/2차 내비게이션을 실제 `Button`/`MenuItem`으로 교체한다.
3. 기존 중앙 `TabControl`과 오른쪽 inspector 내용을 section별 ContentControl 또는 DataTemplate으로 재배치한다.
4. Devices/Calibration/UnityLink/Diagnostics 화면은 기존 command를 먼저 재사용한다.
5. 새 command는 `OpenConfig`, `SaveConfigAs`, `StartUnitySending`, `StopUnitySending`, `ExportDiagnostics`, `StartRecording`, `LoadRecording`, `PauseReplay`, `ScrubReplay` 순서로 추가한다.
6. 각 section별 최소 happy-path 테스트를 추가한다.

### 17.5 현재 Orbbec 운영 기준

- 현재 안정 기준은 `WFOV_2X2BINNED`, `15fps`다.
- `160x160`은 앱 UI preview용 축소 이미지 크기이며 실제 depth stream 해상도가 아니다.
- 실제 K4A body tracking depth stream은 로그에서 `512x512 @ 15fps`로 확인된다.
- USB는 `USB3.x`로 잡혀야 한다. `USB2.x`이면 FPS 저하와 `K4A_WAIT_RESULT_FAILED` 가능성이 높다.

### 17.6 2026-05-22 병렬 구현 완료

이번 구현으로 "보이지만 클릭되지 않던" 상단/2차 내비게이션의 1차 기능화가 완료되었다.

완료된 작업:

- `WorkspaceSection` enum 추가: `Workbench`, `Devices`, `Calibration`, `UnityLink`, `Diagnostics`, `Replay`
- `MainWindowViewModel.SelectedSection` 추가
- `NavigateCommand` 추가
- XAML 선택 상태용 bool property 추가: `IsWorkbenchSelected`, `IsDevicesSelected`, `IsCalibrationSelected`, `IsUnityLinkSelected`, `IsDiagnosticsSelected`, `IsReplaySelected`
- 상단 `Devices`, `Calibration`, `Unity`, `Diagnostics`를 실제 `Button` + `NavigateCommand`로 교체
- 2차 `Workbench`, `Devices`, `Calibration`, `Unity Link`를 실제 `Button` + `NavigateCommand`로 교체
- 선택된 section은 foreground/underline으로 표시
- `Devices`, `Diagnostics`, `Replay`, `Unity Link` 선택 시 기존 중앙 `TabControl`의 관련 탭으로 이동
- `File`을 실제 메뉴로 교체
- `Open config...`, `Reload config`, `Save config`, `Save config as...`, `Revert`, `Exit` 메뉴 추가
- `ConfigFileDialogService` 추가, WPF `OpenFileDialog` / `SaveFileDialog` 사용

변경 파일:

- `src/DSCC.App.Wpf/ViewModels/WorkspaceSection.cs`
- `src/DSCC.App.Wpf/ViewModels/MainWindowViewModel.cs`
- `src/DSCC.App.Wpf/Services/ConfigFileDialogService.cs`
- `src/DSCC.App.Wpf/MainWindow.xaml`

검증:

- `dotnet build .\src\DSCC.App.Wpf\DSCC.App.Wpf.csproj -p:Platform=x64`
- `dotnet build .\DSCC.sln`
- `dotnet test .\DSCC.sln --no-build`

남은 작업:

- `Calibration` 선택 시 중앙 화면을 독립 calibration workspace로 전환
- `Unity Link` 선택 시 Replay 탭이 아니라 독립 Unity Link workspace로 전환
- `Diagnostics` 선택 시 device diagnostics와 log/export 화면을 통합한 독립 diagnostics workspace 구성
- `Open config` 시 live stream이 켜져 있으면 stop/confirm 정책 세분화
- `Start sending` / `Stop sending`, Unity heartbeat/status 수신 구현
- `Export diagnostics` zip 기능 구현
