# Unity Custom Editor Development Handoff

작성일: 2026-05-22  
대상 프로젝트: DSCC Unity Dance App  
연동 프로젝트: `C:\Users\o77do\Developer\DSCC`

## 1. 목적

이 문서는 DSCC에서 송신하는 skeleton 데이터를 Unity의 리깅된 FBX 캐릭터에 적용하기 위한 Unity 런타임 컴포넌트와 커스텀 에디터 개발 범위를 정의한다.

목표는 캐릭터의 월드 위치를 Station anchor에 고정한 상태에서, DSCC skeleton의 머리, 손, 팔, 다리 움직임만 avatar에 반영하는 것이다.

Unity 개발자는 이 문서를 기준으로 다음 기능을 구현한다.

- DSCC UDP MessagePack skeleton 수신
- Station별 avatar 배치 및 anchor 관리
- FBX Humanoid avatar와 DSCC joint 매핑
- Animation Rigging 기반 IK target/hint 자동 구성
- live skeleton, IK target, confidence 상태를 Scene View에서 검증
- neutral pose capture 및 retarget 보정값 저장

## 2. 현재 DSCC 구현 기준

DSCC는 sensor/camera 계층을 담당하고 Unity는 게임, 캐릭터, 연출, 프로젝션을 담당한다. Unity에는 raw camera stream이 아니라 Station 기준으로 정리된 skeleton frame만 전달한다.

현재 주요 계약은 `DSCC.Protocol`에 정의되어 있다.

- `StationSkeletonFrame`
  - `ProtocolVersion`
  - `StationId`
  - `CameraSerial`
  - `DeviceType`
  - `TimestampUsec`
  - `HasPlayer`
  - `State`
  - `Confidence`
  - `IsInsideFootMarker`
  - `IsInsideTrackingRoi`
  - `TrackingLostSeconds`
  - `PelvisLocal`
  - `BodyRotation`
  - `Joints`

- `JointFrameDto`
  - `Name`
  - `PositionLocal`
  - `RotationLocal`
  - `Confidence`

송신 방식:

```text
Skeleton stream: UDP + MessagePack
Default port:    55010
Rule:            Unity는 최신 frame만 사용하고 오래된 frame은 버린다.
```

DSCC의 K4A body tracking source는 joint position을 meter 단위로 변환해서 보낸다. `PositionLocal`은 camera/sensor local 좌표계 기준으로 봐야 하며, Unity 좌표계와 축 방향이 다를 수 있다.

## 3. Unity 쪽 기본 원칙

캐릭터 root는 skeleton의 pelvis 위치를 따라 움직이지 않는다.

대신 Station anchor를 avatar root의 기준 위치로 두고, DSCC joint position은 pelvis 기준 상대 offset으로 변환해서 IK target과 head aim target에 적용한다.

기본 계산식:

```csharp
Vector3 jointUnity = ToUnity(joint.PositionLocal);
Vector3 pelvisUnity = ToUnity(frame.PelvisLocal);
Vector3 relative = jointUnity - pelvisUnity;

target.position = stationAnchor.position
    + stationAnchor.rotation * (relative * avatarScale);
```

이 방식의 의도:

- 사용자가 Station 안에서 조금 움직여도 avatar 전체가 떠다니지 않는다.
- 손, 팔꿈치, 발, 무릎, 머리 방향만 실시간 skeleton에 반응한다.
- Station별 avatar 배치와 projection alignment를 Unity scene에서 안정적으로 관리할 수 있다.

## 4. 권장 Unity 패키지 구조

```text
Assets/DSCC/
  Runtime/
    Protocol/
      StationSkeletonFrame.cs
      JointFrameDto.cs
      Vector3Dto.cs
      QuaternionDto.cs
      StationStateDto.cs
    Networking/
      DsccSkeletonReceiver.cs
      DsccSkeletonFrameBuffer.cs
    Retargeting/
      DsccStationAvatar.cs
      DsccAvatarRetargeter.cs
      DsccRetargetingProfile.cs
      DsccJointName.cs
      DsccCoordinateMapper.cs
    Debugging/
      DsccSkeletonDebugDrawer.cs

  Editor/
    DsccLinkMonitorWindow.cs
    DsccStationAnchorEditor.cs
    DsccRetargetingProfileEditor.cs
    DsccIkRigSetupWizard.cs
    DsccSkeletonSceneDebugger.cs
    DsccNeutralPoseCaptureWindow.cs

  Profiles/
    DefaultRetargetingProfile.asset
    StationLayout.asset
```

`DSCC.Protocol` assembly를 Unity가 직접 참조할 수 있으면 가장 좋다. Unity 호환성 또는 MessagePack resolver 문제가 있으면 Unity용 DTO 복사본을 `Assets/DSCC/Runtime/Protocol` 아래에 둔다.

## 5. 필수 커스텀 에디터

### 5.1 DSCC Link Monitor

메뉴:

```text
Tools/DSCC/Link Monitor
```

역할:

- UDP 수신 시작/중지
- target port 표시 및 변경
- 마지막 수신 timestamp, frame age 표시
- `StationId`, `HasPlayer`, `State`, `Confidence` 표시
- joint count와 joint name 목록 표시
- 오래된 frame 경고
- MessagePack deserialize 오류 표시
- Station별 최신 frame을 JSON-like inspector로 표시

완료 기준:

- DSCC WPF에서 test frame을 보내면 Unity EditorWindow에서 즉시 확인된다.
- 수신은 Editor play mode와 edit mode 중 최소 play mode에서 동작해야 한다.
- 데이터 수신 문제와 avatar retargeting 문제를 분리해서 진단할 수 있어야 한다.

### 5.2 Station Anchor Editor

역할:

- Station 1~4 anchor GameObject 생성
- `StationId`와 avatar prefab 연결
- anchor position/rotation을 Scene View handle로 조정
- anchor 아래에 avatar root를 고정 배치
- DSCC의 `UnityAnchor` 값과 Unity Transform 값을 상호 비교

권장 Scene 구조:

```text
DSCC_Stations
  Station_01
    Anchor
    AvatarRoot
    DebugSkeleton
  Station_02
    Anchor
    AvatarRoot
    DebugSkeleton
```

완료 기준:

- Station별 avatar가 scene에서 명확히 분리된다.
- skeleton frame의 pelvis 이동이 avatar root position을 직접 바꾸지 않는다.
- anchor 회전만 바꿔도 skeleton retarget 방향이 함께 회전한다.

### 5.3 Retargeting Profile Editor

`DsccRetargetingProfile`은 ScriptableObject로 만든다.

포함해야 할 설정:

- DSCC joint name to Unity target mapping
- per-joint confidence threshold
- per-joint smoothing
- avatar scale
- coordinate axis mapping
- mirror X option
- pelvis-relative mode toggle
- low confidence fade out time
- lost player fade out time

기본 매핑:

```text
Head          -> Head aim target
Neck          -> Neck/head reference
ShoulderLeft  -> Left arm reference
ElbowLeft     -> Left elbow hint
WristLeft     -> Left hand IK target
ShoulderRight -> Right arm reference
ElbowRight    -> Right elbow hint
WristRight    -> Right hand IK target
HipLeft       -> Left leg reference
KneeLeft      -> Left knee hint
AnkleLeft     -> Left foot IK target
FootLeft      -> Left foot direction reference
HipRight      -> Right leg reference
KneeRight     -> Right knee hint
AnkleRight    -> Right foot IK target
FootRight     -> Right foot direction reference
```

완료 기준:

- FBX가 바뀌어도 profile만 바꿔서 재사용할 수 있다.
- 좌표 축 뒤집힘, 좌우 반전, scale 오류를 코드 수정 없이 보정할 수 있다.

### 5.4 IK Rig Setup Wizard

메뉴:

```text
Tools/DSCC/Create IK Rig For Selected Avatar
```

역할:

- 선택된 avatar에 `Animator`와 Humanoid avatar가 있는지 검사
- Unity Animation Rigging package 의존성 확인
- `RigBuilder`, `Rig` 자동 생성
- 손, 발 IK target 생성
- 팔꿈치, 무릎 hint 생성
- head aim target 생성
- `DsccAvatarRetargeter`에 target reference 자동 연결

생성 대상:

```text
AvatarRoot
  DSCC_Rig
    Rig
      LeftHandTarget
      LeftElbowHint
      RightHandTarget
      RightElbowHint
      LeftFootTarget
      LeftKneeHint
      RightFootTarget
      RightKneeHint
      HeadAimTarget
```

완료 기준:

- 수동으로 constraint target을 하나씩 만들지 않아도 기본 rig가 구성된다.
- Wizard 실행 후 live skeleton frame을 받으면 손/발/head target이 움직인다.

### 5.5 Live Skeleton Scene Debugger

역할:

- Scene View에 raw DSCC skeleton joint를 점과 선으로 표시
- confidence가 낮은 joint는 다른 색상으로 표시
- pelvis-relative skeleton과 Station anchor 위치를 동시에 표시
- IK target/hint 위치를 표시
- avatar root가 잘못 움직이는 경우 경고 표시

권장 bone line:

```text
Pelvis - SpineNavel - SpineChest - Neck - Head
SpineChest - ShoulderLeft - ElbowLeft - WristLeft
SpineChest - ShoulderRight - ElbowRight - WristRight
Pelvis - HipLeft - KneeLeft - AnkleLeft - FootLeft
Pelvis - HipRight - KneeRight - AnkleRight - FootRight
```

완료 기준:

- raw skeleton, converted skeleton, IK target을 한 화면에서 비교할 수 있다.
- 좌표계 변환이 틀렸을 때 즉시 눈으로 확인할 수 있다.

### 5.6 Neutral Pose Capture Tool

역할:

- 사용자가 정자세로 서 있을 때 현재 skeleton을 neutral pose로 저장
- pelvis 기준 joint offset 저장
- avatar limb length와 skeleton limb length 비율 계산
- head/arm/leg 기본 offset 저장
- 이후 retargeting 보정값으로 사용

완료 기준:

- neutral pose capture 후 T-pose 또는 A-pose FBX 차이를 보정할 수 있다.
- 팔 길이, 다리 길이 차이로 인한 과도한 IK target 위치를 scale로 완화할 수 있다.

## 6. 런타임 컴포넌트

### 6.1 DsccSkeletonReceiver

책임:

- UDP socket open/close
- MessagePack deserialize
- background thread 또는 async receive
- main thread로 최신 frame 전달
- Station별 frame buffer 갱신

주의:

- Unity Transform 갱신은 main thread에서만 수행한다.
- UDP packet 누락은 재전송하지 않는다.
- 오래된 frame은 버린다.
- EditorWindow와 runtime retargeter가 같은 frame buffer를 볼 수 있게 한다.

### 6.2 DsccStationAvatar

책임:

- `StationId`
- `Transform stationAnchor`
- `Animator avatarAnimator`
- `DsccAvatarRetargeter retargeter`
- `DsccRetargetingProfile profile`
- `HasPlayer` 상태에 따른 avatar visibility 또는 rig weight 제어

### 6.3 DsccAvatarRetargeter

책임:

- latest `StationSkeletonFrame` 적용
- joint confidence 확인
- pelvis-relative offset 계산
- coordinate mapping 적용
- smoothing 적용
- IK target/hint Transform 갱신
- `HasPlayer == false` 또는 `State == Lost/Exited`일 때 weight fade out

기본 update 시점:

```text
Update:
  latest frame read
  target position smoothing

LateUpdate:
  IK target Transform finalize
```

## 7. 좌표계 및 리타게팅 규칙

초기 좌표 변환은 profile에서 조절 가능해야 한다.

예시 시작값:

```csharp
static Vector3 ToUnity(Vector3Dto v)
{
    return new Vector3(v.X, -v.Y, v.Z);
}
```

다만 실제 Orbbec/K4A sensor orientation, Unity scene forward 방향, Station anchor 회전에 따라 다음 옵션이 필요하다.

- axis remap: `x/y/z` 입력을 Unity `x/y/z` 중 어디에 넣을지
- sign flip: 각 축별 `+/-`
- global yaw offset
- mirror X
- meter scale
- pelvis-relative mode

리타게팅은 1차 구현에서 position 기반 IK를 우선한다. `RotationLocal`은 real camera에서는 값이 들어오지만, mock replay에서는 identity일 수 있으므로 rotation-only 방식으로 시작하면 테스트가 어렵다.

## 8. Confidence 및 상태 처리

권장 기본값:

```text
Joint confidence threshold: 0.45
Frame confidence threshold: 0.45
Smoothing half-life:        0.06s ~ 0.12s
Lost fade out:              0.25s ~ 0.5s
Exited hide delay:          0.5s
```

처리 규칙:

- `HasPlayer == false`: rig weight를 0으로 fade out
- `State == Empty`: avatar idle 또는 hidden
- `State == Entering`: rig weight fade in
- `State == Active`: full tracking
- `State == Lost`: 마지막 유효 pose 유지 후 fade out
- `State == Exited`: avatar reset 또는 hidden
- joint confidence가 낮은 target은 마지막 유효값 유지 또는 해당 constraint weight 감소

## 9. 개발 순서

### Phase 1: 수신 및 모니터

- Unity DTO 정의 또는 `DSCC.Protocol` 참조
- MessagePack dependency 추가
- `DsccSkeletonReceiver` 구현
- `DsccLinkMonitorWindow` 구현
- DSCC WPF test frame 수신 확인

완료 조건:

- `StationId`, `HasPlayer`, `State`, `Confidence`, joint count를 Unity에서 볼 수 있다.

### Phase 2: Scene Debugger

- Station anchor GameObject 생성
- latest frame을 Scene View gizmo로 표시
- pelvis-relative skeleton 표시
- coordinate mapping profile 적용

완료 조건:

- raw skeleton이 Unity scene의 Station 위치 근처에 올바른 방향으로 표시된다.

### Phase 3: Avatar Retargeting

- `DsccRetargetingProfile` 구현
- IK target/hint Transform 연결
- 손/발/head target 갱신
- smoothing 및 confidence 처리

완료 조건:

- avatar root는 고정되고 손, 팔, 다리, 머리만 tracking data에 반응한다.

### Phase 4: Rig Setup Wizard

- Animation Rigging 자동 구성
- constraint target 자동 연결
- profile validation 추가

완료 조건:

- 새 FBX avatar를 선택하고 Wizard를 실행하면 기본 retarget 준비가 끝난다.

### Phase 5: Neutral Pose Capture

- live frame capture
- neutral offset 저장
- limb scale 계산
- profile에 보정값 반영

완료 조건:

- 사용자 체형과 avatar 비율 차이를 profile에서 완화할 수 있다.

## 10. 테스트 시나리오

필수 테스트:

- DSCC WPF에서 `Send one active StationSkeletonFrame`을 실행했을 때 Unity Link Monitor에 frame이 표시된다.
- `MockReplay` 또는 replay frame을 송신하면 Scene Debugger의 skeleton이 움직인다.
- avatar root position은 Station anchor에서 변하지 않는다.
- 손목 joint가 움직이면 hand IK target이 움직인다.
- 발목 joint가 움직이면 foot IK target이 움직인다.
- `HasPlayer == false` frame 수신 시 rig weight가 fade out된다.
- confidence가 낮은 joint는 jitter를 만들지 않는다.
- Station 1과 Station 2가 동시에 들어와도 각 avatar가 자기 Station frame만 사용한다.

## 11. 외부 의존성

Unity package:

- Animation Rigging
- MessagePack for CSharp 또는 Unity 호환 MessagePack 패키지

선택:

- Newtonsoft Json: debug dump용
- Odin Inspector: 사용 가능하면 profile editor 작성 속도 개선

의존성 관련 주의:

- MessagePack resolver 설정은 Unity IL2CPP 빌드를 고려해야 한다.
- Editor-only 코드와 runtime 코드를 assembly definition으로 분리한다.
- UDP receiver는 editor domain reload와 play mode 종료 시 socket을 확실히 닫아야 한다.

## 12. 열려 있는 결정 사항

- Unity가 `DSCC.Protocol.dll`을 직접 참조할지, Unity DTO 복사본을 둘지 결정해야 한다.
- Unity package를 별도 UPM package로 만들지, Unity 프로젝트 내부 `Assets/DSCC`로 둘지 결정해야 한다.
- `RotationLocal`을 어느 phase에서 본 회전에 섞을지 결정해야 한다.
- Station anchor 값을 DSCC config에서 export/import할지, Unity scene을 source of truth로 둘지 결정해야 한다.
- multi-avatar 동시 tracking에서 visibility, fade, spawn/despawn 연출 정책을 Unity gameplay 쪽과 합의해야 한다.

## 13. 인수 기준

다음 조건을 만족하면 Unity custom editor MVP는 개발 핸드오프 완료로 본다.

- Unity Editor에서 DSCC skeleton UDP frame을 수신하고 상태를 확인할 수 있다.
- Scene View에서 Station anchor, raw skeleton, IK target을 동시에 볼 수 있다.
- 리깅된 FBX avatar에 대해 Wizard로 기본 IK rig를 생성할 수 있다.
- avatar root는 고정된 채 머리, 손, 팔, 다리가 skeleton data에 반응한다.
- Retargeting Profile에서 joint mapping, coordinate axis, smoothing, confidence threshold를 코드 수정 없이 조정할 수 있다.
- player lost/exited 상태에서 rig weight 또는 avatar visibility가 안정적으로 정리된다.

