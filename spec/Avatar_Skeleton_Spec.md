# DSCC 아바타 스켈레톤 명세 (3D 애니메이터/리거용)

작성일: 2026-06-11
대상: 아바타 FBX를 제작/리깅하는 3D 애니메이터, Unity 리타게팅 담당 개발자
선행 문서: `spec/Orbbec_Unity_Retargeting_Research.md`, `spec/Unity_Custom_Editor_Handoff.md`

## 1. 목적

DSCC는 Orbbec Femto Bolt/Mega 카메라에서 Azure Kinect Body Tracking(K4ABT) 32-joint 스켈레톤을 추출해 Unity로 UDP 송신한다. Unity는 이 데이터를 **리깅된 FBX 아바타**에 리타게팅한다.

이 문서는 그 아바타가 **어떤 본 구조를 가져야 데이터를 받아 움직일 수 있는지**를 정의한다. 애니메이터는 이 문서만 보고 본을 잡을 수 있어야 한다.

핵심 전제 (연구 문서의 결정 사항):

- 트래킹 데이터의 본 길이는 **실제 사람의 관측치**다. 아바타 본 길이의 기준이 아니다. 아바타 비율은 FBX 리그가 소유한다.
- Unity는 아바타 본을 직접 덮어쓰지 않고, **pelvis 기준 상대 위치로 IK target/hint를 움직이는 방식**으로 구동한다 (Animation Rigging).
- 아바타 root는 Station anchor에 고정된다. 사람이 Station 안에서 움직여도 아바타 전체가 떠다니지 않는다.

## 2. DSCC가 보내는 스켈레톤 데이터

| 항목 | 값 |
|---|---|
| Joint 수 | 32개 (K4ABT 표준, 아래 표) |
| 위치 단위 | meter (`float`) |
| 위치 좌표계 | **depth 센서 좌표계**: +X 카메라 기준 오른쪽, +Y 아래, +Z 카메라 전방 |
| 회전 | 센서 좌표계 기준 **절대(글로벌) 쿼터니언**. 부모 본 기준 로컬 회전이 아님 |
| Confidence | joint별 0.0 / 0.33 / 0.66 / 1.0 (None/Low/Medium/High). 현행 SDK는 사실상 Medium까지만 출력 |
| 프레임레이트 | 카메라당 최대 15fps (현행 설정) |
| 전송 | UDP MessagePack, `StationSkeletonFrame` (Station당 1명) |

주의: `JointFrameDto.PositionLocal`/`RotationLocal`이라는 이름과 달리 둘 다 **센서 공간** 값이다. Unity 좌표 변환(축 리맵, Y 부호 반전, mirror)은 Unity 쪽 `DsccCoordinateMapper`가 담당한다.

Mirror 옵션(`mirrorSkeletonX`, 전면 카메라용)이 켜져 있으면 DSCC가 송신 전에 X를 반전하고 joint 이름의 Left/Right를 맞바꿔 보낸다. Unity/애니메이터 입장에서는 항상 일관된 좌우의 스켈레톤을 받는 것으로 보면 된다.

## 3. 32-Joint 목록 (와이어 이름 기준)

`Name` 컬럼 문자열이 UDP 패킷에 실제로 들어가는 값이다 (대소문자 포함).

| # | Name | 부모 | 아바타 구동 용도 |
|---|---|---|---|
| 0 | `Pelvis` | (root) | 모든 joint의 상대좌표 기준점. 아바타 root는 따라가지 않음 |
| 1 | `SpineNavel` | Pelvis | 상체 기울기 참조 (보조) |
| 2 | `SpineChest` | SpineNavel | 상체 기울기/어깨 라인 참조 (보조) |
| 3 | `Neck` | SpineChest | 머리 방향 보조 참조 |
| 4 | `ClavicleLeft` | SpineChest | 미구동 (참조용) |
| 5 | `ShoulderLeft` | ClavicleLeft | 왼팔 체인 참조 |
| 6 | `ElbowLeft` | ShoulderLeft | **왼팔꿈치 IK hint** |
| 7 | `WristLeft` | ElbowLeft | **왼손 IK target** |
| 8 | `HandLeft` | WristLeft | 손 방향 보조 |
| 9 | `HandTipLeft` | HandLeft | 미구동 |
| 10 | `ThumbLeft` | WristLeft | 미구동 |
| 11 | `ClavicleRight` | SpineChest | 미구동 (참조용) |
| 12 | `ShoulderRight` | ClavicleRight | 오른팔 체인 참조 |
| 13 | `ElbowRight` | ShoulderRight | **오른팔꿈치 IK hint** |
| 14 | `WristRight` | ElbowRight | **오른손 IK target** |
| 15 | `HandRight` | WristRight | 손 방향 보조 |
| 16 | `HandTipRight` | HandRight | 미구동 |
| 17 | `ThumbRight` | WristRight | 미구동 |
| 18 | `HipLeft` | Pelvis | 왼다리 체인 참조 |
| 19 | `KneeLeft` | HipLeft | **왼무릎 IK hint** |
| 20 | `AnkleLeft` | KneeLeft | **왼발 IK target** |
| 21 | `FootLeft` | AnkleLeft | 발끝 방향 참조 |
| 22 | `HipRight` | Pelvis | 오른다리 체인 참조 |
| 23 | `KneeRight` | HipRight | **오른무릎 IK hint** |
| 24 | `AnkleRight` | KneeRight | **오른발 IK target** |
| 25 | `FootRight` | AnkleRight | 발끝 방향 참조 |
| 26 | `Head` | Neck | **Head aim target** |
| 27 | `Nose` | Head | 머리 방향 1순위 소스 (confidence 게이트 필수) |
| 28 | `EyeLeft` | Head | 미구동 |
| 29 | `EarLeft` | Head | 미구동 |
| 30 | `EyeRight` | Head | 미구동 |
| 31 | `EarRight` | Head | 미구동 |

굵게 표시한 9개(손×2, 발×2, 팔꿈치/무릎 hint×4, head aim)가 실제 아바타를 움직이는 핵심 채널이다. 나머지는 참조/보정용이며, 트래킹이 제공하지 않는 디테일(손가락, 발가락, 얼굴)은 아바타 쪽 애니메이션이 소유한다.

## 4. 아바타 리깅 요구사항 (체크리스트)

### 4.1 필수

- [ ] **Unity Humanoid 호환 계층**으로 리깅한다. 최소 본:
  `Hips → Spine → Chest → Neck → Head`,
  좌우 `Shoulder(옵션) → UpperArm → LowerArm → Hand`,
  좌우 `UpperLeg → LowerLeg → Foot`
- [ ] Unity 임포트 시 Animation Type = **Humanoid**로 Avatar 매핑이 에러 없이 통과해야 한다 (Configure에서 빨간 본 없음).
- [ ] **T-pose**로 제작한다 (A-pose만 가능하면 Unity에서 Enforce T-Pose로 보정 가능한 범위 내).
- [ ] 단위 **1 unit = 1 m**, 모든 본 scale = 1 (비균일/음수 스케일 금지). Freeze transform 후 납품.
- [ ] 캐릭터는 **+Z 전방, +Y 상방**을 보도록 정렬.
- [ ] 본 이름은 자유지만 좌우 구분이 일관되어야 한다 (`_L/_R`, `Left/Right` 등 한 가지 규칙). Mixamo/Rigify 등 표준 네이밍이면 자동 매핑이 쉬움.
- [ ] root 본(Hips의 부모, 위치 이동용)을 메시 원점(바닥, 발 사이)에 둔다. 아바타 root는 Station anchor에 고정되므로 root 본에 베이크된 오프셋이 없어야 한다.

### 4.2 권장

- [ ] `UpperChest`(SpineChest 대응)까지 척추 3단 구성 — 상체 기울기 표현이 좋아진다.
- [ ] 팔꿈치/무릎은 **단일 축으로 자연스럽게 굽는 roll 정렬** — IK hint로 구동되므로 본 roll이 비틀려 있으면 관절이 꺾인다.
- [ ] 손가락/발가락 본은 자유 — 트래킹으로 구동되지 않으며, 포즈/루프 애니메이션으로만 사용된다.
- [ ] 날개·꼬리·귀 등 비인간 부속은 별도 본 체인으로 분리하고 Humanoid 매핑에 포함하지 않는다 (Generic 보조 애니메이션 또는 물리로 처리).

### 4.3 스타일라이즈드(비인간 비율) 캐릭터 주의

연구 문서의 결정 사항을 따른다:

- 사람과 비율이 다른 캐릭터(머리 큰 마스코트, 긴 날개 팔 등)는 **하나의 글로벌 스케일로 맞추지 않는다**. Unity 리타게팅 프로파일에서 몸통/팔다리/머리 스케일을 분리해 보정한다.
- 따라서 애니메이터는 비율을 트래킹에 맞춰 변형할 필요가 없다. **캐릭터 고유 비율 그대로 리깅**하고, 대신 위 체크리스트(T-pose, roll 정렬, Humanoid 매핑)를 지키면 된다.
- 머리/목 본은 실측 비율에 가깝게(과장 스케일 없이) 유지하는 것이 head aim 품질에 유리하다.

## 5. K4ABT → Unity Humanoid 매핑 참조표

Unity 개발자가 리타게팅 프로파일을 만들 때의 기준 매핑.

| DSCC Joint | Unity Humanoid Bone | 구동 방식 |
|---|---|---|
| Pelvis | Hips | 직접 구동 안 함 (상대좌표 기준점) |
| SpineNavel | Spine | 참조 |
| SpineChest | Chest / UpperChest | 참조 |
| Neck | Neck | 참조 |
| Head | Head | Aim constraint (Nose→Head 방향 우선순위 규칙은 연구 문서 §2) |
| ClavicleL/R | Left/Right Shoulder | 미구동 |
| ShoulderL/R | Left/Right Upper Arm | TwoBoneIK root |
| ElbowL/R | Left/Right Lower Arm | TwoBoneIK hint |
| WristL/R | Left/Right Hand | TwoBoneIK target |
| HipL/R | Left/Right Upper Leg | TwoBoneIK root |
| KneeL/R | Left/Right Lower Leg | TwoBoneIK hint |
| AnkleL/R | Left/Right Foot | TwoBoneIK target |
| FootL/R | Left/Right Toes(있으면) | 발끝 방향 참조 |
| Hand/HandTip/Thumb, Eye/Ear | (매핑 없음) | 미구동 |

## 6. Confidence 처리 규칙 (요약)

리타게팅 구현이 반드시 지켜야 하는 규칙 (연구 문서 §5):

- Medium(0.66) 이상: 정상 업데이트
- Low(0.33): 위치는 스무딩 강화, 회전 민감 joint(머리)는 hold-last
- None(0.0): 업데이트하지 않음
- 얼굴 joint(Nose/Eye/Ear)는 손보다 **엄격한 임계값** 적용 — 작은 오차가 큰 머리 회전을 만든다
- `HasPlayer == false` 또는 `State == Lost/Exited`: rig weight를 fade out하고 idle/기본 포즈로 복귀

## 7. 납품/검수 기준

1. Unity Humanoid 임포트 시 Avatar 매핑 자동 인식, 수동 교정 없이 통과
2. T-pose 검증 통과 (Unity Muscles & Settings에서 기형 없음)
3. IK Rig Setup Wizard(핸드오프 문서 §5.4) 실행 시 손/발 target, 팔꿈치/무릎 hint가 정상 생성
4. Mock replay 프레임 수신 시: root 고정 상태에서 손/발/머리만 추적 데이터에 반응
5. 손가락/부속 본이 트래킹과 충돌하지 않음 (별도 애니메이션 레이어에서 동작)
