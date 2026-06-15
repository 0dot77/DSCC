# DSCC

Dance Station Control Center(DSCC)는 Orbbec Femto Bolt/Mega 기반의 댄스 스테이션 운영용 WPF 앱입니다. 카메라 연결, Station 매핑, Depth/IR/Color 프리뷰, Skeleton 추적 상태 확인, ROI/발 마커 캘리브레이션, Unity 송신을 한 화면에서 관리합니다.

## 현재 범위

- Orbbec Femto Bolt/Mega 장치 검색 및 Station 자동 할당
- 멀티 카메라(스테이션당 1대, 최대 4대) 동시 body tracking
- Orbbec SDK v2 기반 Depth/IR/Color 라이브 프리뷰
- Azure Kinect K4A wrapper 기반 body tracking (Cuda/DirectML/Cpu 폴백 체인, lite 모델 옵션)
- Sticky body 선택: ROI 안의 사람을 body ID 기준으로 고정 추적 (다른 사람이 화면에 들어와도 타깃 유지)
- Depth 프리뷰 위 Skeleton joint/bone 오버레이
- Station별 ROI, 발 마커, Unity anchor 설정
- `StationSkeletonFrame` UDP MessagePack 송신 (Unity anchor 포함)
- Unity 이벤트 UDP JSON 송신
- 전면 카메라용 skeleton X mirror 토글
- Head/Neck rotation 안정화 필터
- Replay/test frame 송신
- WPF 운영 UI, 로그, 진단 패널 (프레임 평가/송신은 백그라운드 스레드, UI는 코얼레싱 갱신)

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

1. Orbbec 카메라를 USB 3.x 포트에 연결합니다. **여러 대를 쓸 때는 서로 다른 USB 컨트롤러(루트 허브)에 분산하세요.** 한 컨트롤러에 몰리면 대역폭 부족으로 프레임이 떨어집니다. 연결 후 Orbbec 로그/장치 관리자에서 실제로 USB 3.x로 붙었는지 확인하세요 (USB 2.x로 붙으면 depth 품질과 fps가 크게 떨어집니다).
2. `.\DSCC.Live.x64.cmd`로 DSCC를 실행합니다.
3. `Refresh Orbbec devices`로 장치를 검색합니다.
4. **Devices 탭의 Station 콤보박스에서 각 시리얼을 원하는 Station에 고정**합니다(0 = 할당 해제, 같은 시리얼은 한 스테이션에만 — 다른 곳에 있으면 자동 해제). 일괄 매핑이 필요하면 `Auto assign`을 사용해도 됩니다. 고정 후 `Save config`로 저장하면 다음 실행부터 그대로 유지됩니다.
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
  "bodyTracking": {
    "processingModes": ["Cuda", "DirectML", "Cpu"],
    "useLiteModel": true,
    "maxFps": 15,
    "gpuDeviceId": 0,
    "previewIntervalMilliseconds": 150
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

- `autoAssignDevicesOnStart`: 기본 false. true면 live 시작 시 미할당 장치를 빈 Station에 자동으로 채웁니다(무인 운용용). false면 저장된 고정 시리얼만 사용합니다.
- `device.serial`: Station에 고정할 Orbbec serial (Devices 탭에서 선택·저장 가능)
- `device.depthMode`: Depth FOV/해상도 모드
- `device.fps`: 카메라 FPS
- `calibration.footMarkerCenter`: 발 마커 중심
- `calibration.trackingRoi`: 사람을 추적할 공간 범위
- `calibration.unityAnchor`: Unity 공간으로 보낼 기준 위치/회전 (송신 프레임에도 포함됨)
- `unity.mirrorSkeletonX`: 전면 카메라 기준 좌우 반전
- `unity.stabilizeHeadRotation`: Head/Neck rotation 튐 완화
- `thresholds.requireFootMarkerWhileActive`: 기본 false. 발 마커는 입장 게이트로만 쓰고, Active 상태에서는 ROI 안에만 있으면 추적을 유지합니다. true로 바꾸면 춤추다 마커를 벗어나도 추적이 풀리는 예전 동작이 됩니다.

### bodyTracking 설정

- `processingModes`: 스테이션별 트래커 시작 시 순서대로 시도합니다. 기본 `["Cuda", "DirectML", "Cpu"]`. CUDA 런타임 DLL이 없으면 Cuda는 자동으로 건너뛰고 로그에 안내가 남습니다.
- `useLiteModel`: k4abt lite 모델(`dnn_model_2_0_lite_op11.onnx`) 사용. **카메라 4대를 한 GPU에서 돌릴 때 강력 권장** (full 모델은 인스턴스당 부하가 큼).
- `maxFps`: body tracking 파이프라인의 FPS 상한. 스테이션 `device.fps`와 이 값 중 작은 쪽이 적용됩니다. CUDA + lite 모델로 4대 안정 확인 후 30까지 올려볼 수 있습니다.
- `previewIntervalMilliseconds`: depth/IR 프리뷰 생성 간격. 0이면 매 프레임 생성합니다.

### CUDA 모드 준비 (RTX GPU 권장 경로)

k4abt 1.1.x의 Cuda 모드는 CUDA 11.4.x + cuDNN 8.2.x 런타임 DLL이 필요하며 NuGet으로 배포되지 않습니다. 다음 중 하나로 준비합니다.

1. [Azure Kinect Body Tracking SDK 1.1.2 MSI](https://learn.microsoft.com/en-us/azure/kinect-dk/body-sdk-download) 설치 후 `tools` 폴더를 PATH에 추가
2. `cudart64_110.dll`, `cublas64_11.dll`(+`cublasLt64_11.dll`), `cufft64_10.dll`, `cudnn64_8.dll`(+`cudnn_ops_infer64_8.dll`, `cudnn_cnn_infer64_8.dll`)을 `third_party/cuda-runtime-win-x64/bin/`에 복사 — 빌드 시 앱 출력 폴더로 자동 복사됩니다

DLL이 없으면 DirectML로 자동 폴백되므로 동작 자체는 합니다. 다만 4대 동시 운용 성능은 CUDA + lite 모델 조합으로 검증하는 것을 권장합니다.

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

### ROI 좌표계 주의 (Y축)

ROI와 발 마커는 **K4A depth 카메라 좌표계** 기준입니다: +X 카메라 오른쪽, **+Y 아래 방향**, +Z 카메라 전방. 즉 `trackingRoi.minY/maxY`는 위쪽이 음수, 아래쪽이 양수입니다. 카메라가 사람 골반보다 높게 설치된 일반적인 구성에서는 골반 Y가 0~+0.6 근처라 `minY: 0, maxY: 2.4`가 통과하지만, 카메라를 낮게 설치하거나 위로 틸트하면 골반 Y가 음수가 되어 ROI 판정에서 빠질 수 있습니다. 이때는 `minY`를 -1.0 정도로 내려서 현장 캘리브레이션하세요. 가장 확실한 방법은 사람을 세워 두고 Station 카드의 pelvis Y 값을 읽은 뒤 ROI Y 범위를 잡는 것입니다.

### 두 명 이상이 카메라에 잡힐 때

body 선택은 ROI 안에 골반이 있는 사람을 우선하고, 한 번 선택한 사람은 tracker body ID로 고정(sticky)합니다. 플레이어가 ROI를 벗어나고 다른 사람이 ROI에 들어오면 그 사람으로 전환됩니다. ROI를 실제 스테이션 영역에 맞게 좁게 잡을수록 안정적입니다. ROI 변경은 live 재시작 후 body 선택에 반영됩니다.

### Unity에서 왼손/오른손이 반대로 움직임

Unity Link 패널에서 `Mirror skeleton X for front-facing camera`를 켭니다. 설정 저장 후 live/replay를 다시 시작하면 mirror가 송신 프레임에 적용됩니다.

## 개발 참고 문서

- `spec/DSCC_Development_Handoff.md`
- `spec/Unity_Custom_Editor_Handoff.md`
- `spec/Avatar_Skeleton_Spec.md` — 3D 애니메이터/리거용 아바타 본 명세 (32 joint, Humanoid 매핑, 리깅 체크리스트)
- `spec/Avatar_Rig_Validation.md` — 디자이너 아바타 FBX 3종 리그 검증 결과와 재export 가이드
- `spec/Orbbec_Unity_Retargeting_Research.md`
- `spec/design/dscc_wpf_mockup.html`
