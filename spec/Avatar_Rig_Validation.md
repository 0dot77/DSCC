# 디자이너 아바타 리그 검증 결과

검증일: 2026-06-11
재작업: 2026-06-11 — 새대갈/슘당의 deform-only 버전을 Blender 헤드리스로 생성 (§재작업 결과 참조)
대상: `C:\Users\o77do\Developer\Avatars`의 FBX 3종
방법: FBX 바이너리(버전 7400)에서 본 노드 트리, 부모-자식 연결, 스킨 클러스터 연결을 직접 파싱해 DSCC 스켈레톤 데이터(K4A 32 joint, 구동 채널 9개)와의 호환성을 판정. 기준 명세는 `spec/Avatar_Skeleton_Spec.md`.

## 요약

| 모델 | 본 구조 | Unity Humanoid | DSCC 9채널 구동 | 판정 |
|---|---|---|---|---|
| 진돌_유니티리그.fbx | 25노드, 깨끗한 단일 체인, Unity 표준 네이밍 | ✅ 자동 매핑 가능 | ✅ 전부 대응 | **그대로 사용 가능** |
| 새대갈.fbx | 872노드 (Rigify 컨트롤 리그 통째 export), DEF 체인은 연속 | ⚠️ 수동 매핑으로 가능 | ✅ 전부 대응 | 사용 가능, 재export 권장 |
| 슘당.fbx | 885노드 (Rigify 통째 export), **DEF 체인이 분산됨** | ❌ 현재 export로는 불가 | ⚠️ Animation Rigging 직접 본 IK로만 가능 | **재export 필요** |

세 모델 모두 애니메이션 스택 없음(포즈만 포함). 렌더 메시는 `body` 단일 메시.

## 1. 진돌_유니티리그.fbx — 합격

```text
UnityRig_Metarig
  Root
    Hips
      Spine → Chest → UpperChest
        LeftShoulder → LeftUpperArm → LeftLowerArm → LeftHand
        RightShoulder → RightUpperArm → RightLowerArm → RightHand
        Neck → Head
      LeftUpperLeg → LeftLowerLeg → LeftFoot → LeftToes
      RightUpperLeg → RightLowerLeg → RightFoot → RightToes
```

- 23본 전부 스킨 클러스터에 연결됨 (전부 실제 변형 본).
- 본 이름이 Unity Humanoid 명칭과 1:1 — 임포트 시 자동 매핑이 그대로 통과할 구조.
- 손가락/얼굴 본 없음: DSCC가 손가락을 구동하지 않으므로 무관 (명세 §4.2).
- DSCC 매핑: `WristL/R→LeftHand/RightHand`, `ElbowL/R→LeftLowerArm/...`, `AnkleL/R→LeftFoot/...`, `KneeL/R→LeftLowerLeg/...`, `Head→Head`. IK Rig Setup Wizard를 바로 적용할 수 있는 기준 모델.

## 2. 새대갈.fbx — 사용 가능, 정리 권장

Blender Rigify 리그가 **컨트롤 본(MCH-, ORG-, *_ik, VIS_ 등) 포함 통째로** export된 상태(872노드, 이 중 변형에 실제 쓰이는 것은 DEF- 계열).

다행히 **DEF 변형 체인은 연속적**으로 붙어 있다:

```text
root
  DEF-spine (hips)
    DEF-spine.001 → DEF-spine.002 → DEF-spine.003
      DEF-shoulder.L → DEF-upper_arm.L → DEF-forearm.L → DEF-hand.L (+손가락 DEF-f_*, DEF-thumb.*)
      DEF-shoulder.R → … → DEF-hand.R
      DEF-spine.004 → DEF-neck → DEF-head (얼굴 DEF 본 다수: brow/cheek/chin/ear/jaw…)
    DEF-thigh.L → DEF-shin.L → DEF-foot.L → DEF-toe.L
    DEF-thigh.R → DEF-shin.R → DEF-foot.R → DEF-toe.R
```

- Humanoid 매핑(수동): Hips=`DEF-spine`, Spine=`DEF-spine.001`, Chest=`DEF-spine.002`, UpperChest=`DEF-spine.003`, Neck=`DEF-neck`, Head=`DEF-head`, 팔=`DEF-upper_arm/forearm/hand`, 다리=`DEF-thigh/shin/foot/toe`. 자동 매핑은 ORG-/MCH- 본을 잘못 집을 수 있으므로 **반드시 수동 확인**.
- DSCC 9채널 전부 대응 가능.
- 문제점: 컨트롤 본 ~600개가 Unity에서 죽은 노드로 남는다(Blender 컨스트레인트는 FBX로 오지 않음). 계층이 비대해 실수 유발 + 미세한 런타임 비용.
- **권장**: Blender에서 FBX export 시 Armature ▸ "Only Deform Bones" 체크(+필요시 leaf bone 끄기)로 DEF 본만 재export. 기존 스킨이 DEF 본에 걸려 있으므로 무손실.

## 3. 슘당.fbx — 재export 필요

같은 Rigify 통째 export(885노드)인데, 이쪽은 **DEF 체인이 컨트롤 계층 안에 흩어져 있다**:

- `DEF-thigh.L/R`이 `ORG-spine`(컨트롤 체인 깊숙한 곳) 아래에 붙어 있음 — `DEF-spine`의 자식이 아님
- `DEF-upper_arm.L/R`이 `ORG-shoulder.L/R` 아래에 붙어 있고, `DEF-shoulder.L/R`은 팔과 무관한 잎(leaf) 본
- `DEF-spine → DEF-spine.001 … DEF-spine.006` 척추 체인은 `root` 아래에 **팔다리 없이 단독으로** 존재
- 목/머리 전용 본이 없음: Rigify 기본 척추 규칙대로 `DEF-spine.004/.005`=목, `DEF-spine.006`=머리
- 팔다리는 트위스트 분절 포함: `DEF-thigh.L → DEF-thigh.L.001 → DEF-shin.L → DEF-shin.L.001 → DEF-foot.L`

영향:

- **Unity Humanoid 불가**: Humanoid는 UpperLeg가 Hips의 자손이어야 하는데, `DEF-thigh.L`의 조상 경로가 `DEF-spine`을 지나지 않는다. Avatar 구성이 실패하거나 잘못된 본을 집는다.
- **Animation Rigging 직접 본 IK로는 구동 가능**: 각 사지 체인 자체는 연속(TwoBoneIK root/mid/tip 요건 충족 — 트위스트 .001 본은 중간 통과 본으로 허용). DSCC Unity 설계(핸드오프 §5.4, IK target/hint 방식)라면 현재 파일로도 손/발/머리 구동은 된다. 단 척추 기울기 표현과 유지보수성이 나쁘다.
- **권장(둘 중 하나)**:
  1. Blender에서 "Only Deform Bones"로 재export — 이때 Rigify는 DEF 본 부모를 자동 재구성해 연속 체인으로 내보낸다 (새대갈과 같은 형태가 됨). 가장 간단.
  2. 그대로 쓰려면 Unity에서 Humanoid 포기, 직접 본 매핑 프로파일: Pelvis=`DEF-spine`, Neck=`DEF-spine.004`, Head=`DEF-spine.006`, 손=`DEF-hand.L/R`, 팔꿈치=`DEF-forearm.L/R`, 발=`DEF-foot.L/R`, 무릎=`DEF-shin.L/R`.

## 공통 확인 사항 (Unity 임포트 시)

1. 세 모델 모두 루트에 -90° X 회전(Blender→FBX 관례)이 있다. 임포트 후 본 로컬축이 틀어졌으면 export 시 "Apply Transform" 옵션을 조정.
2. 레스트 포즈가 T-pose인지 임포트 후 확인 (FBX 로컬 회전값만으로는 단정 불가). A-pose면 Humanoid Configure에서 Enforce T-Pose.
3. 스케일: Unity에서 캐릭터 키가 실제 의도 크기(m)로 들어오는지 확인. DSCC는 미터 단위로 보낸다.
4. 비인간 비율(새대갈 부리/날개 등)은 `spec/Orbbec_Unity_Retargeting_Research.md`의 per-segment scale 규칙을 따를 것 — 글로벌 스케일 금지.

## 재작업 결과 (2026-06-11)

새대갈/슘당을 Blender 5.1 헤드리스 스크립트로 재작업해 같은 폴더에 새 파일로 출력했다. **원본은 그대로 두었다.**

| 출력 파일 | 본 수 | 결과 |
|---|---|---|
| `새대갈_deform.fbx` | 555 → 149 | DEF 본만 유지, 얼굴 본을 `DEF-head` 아래로 정리 |
| `슘당_deform.fbx` | 566 → 161 | DEF 본만 유지 + **끊어진 체인 재연결** (thigh→DEF-spine, upper_arm→DEF-shoulder, shoulder→DEF-spine.003), 얼굴 본을 `DEF-spine.006`(머리) 아래로 정리 |

수행한 작업:

1. MCH-/ORG-/IK/VIS 등 컨트롤 본 전부 삭제, `DEF-*`와 `root`만 유지
2. 부모 재결정 규칙: 원래 조상 체인을 따라가며 ① DEF 조상 → 그대로, ② `ORG-x` 조상이고 `DEF-x` 존재 → 그쪽, ③ 컨트롤 본 `x`이고 `DEF-x` 존재 → 그쪽 (예: head 컨트롤 → DEF-head), 모두 실패 시 `root`
3. 얼굴 DEF 본(brow/lid/cheek/chin/ear/jaw/lip/nose/forehead/temple/teeth/tongue/eye)은 머리 본에 직접 고정 — 머리 회전을 그대로 따라가며, 표정 애니메이션이 없는 본 앱에는 올바른 동작
4. 본의 head/tail/roll(레스트 포즈)과 스킨 가중치는 일절 수정하지 않음
5. 안전장치: 실제 가중치가 실린 vertex group이 삭제 대상 본에 있으면 중단 — 두 파일 모두 통과 (`weighted_groups_missing_after_prune: []`)

출력 FBX를 다시 파싱해 구조 확인 완료: 두 파일 모두 `root → DEF-spine → …` 단일 연속 체인이며, Humanoid 필수 본 검증을 통과하는 구조다.

### Unity Humanoid 매핑표 (재작업 파일 기준)

| Humanoid Bone | 새대갈_deform | 슘당_deform |
|---|---|---|
| Hips | DEF-spine | DEF-spine |
| Spine | DEF-spine.001 | DEF-spine.001 |
| Chest | DEF-spine.002 | DEF-spine.002 |
| UpperChest | DEF-spine.003 | DEF-spine.003 |
| Neck | DEF-neck | DEF-spine.004 (.005는 중간 본으로 미매핑) |
| Head | DEF-head | DEF-spine.006 |
| Shoulder L/R | DEF-shoulder.L/R | DEF-shoulder.L/R |
| Upper Arm L/R | DEF-upper_arm.L/R | DEF-upper_arm.L/R (.001 트위스트 미매핑) |
| Lower Arm L/R | DEF-forearm.L/R | DEF-forearm.L/R (.001 트위스트 미매핑) |
| Hand L/R | DEF-hand.L/R | DEF-hand.L/R |
| Upper Leg L/R | DEF-thigh.L/R | DEF-thigh.L/R (.001 미매핑) |
| Lower Leg L/R | DEF-shin.L/R | DEF-shin.L/R (.001 미매핑) |
| Foot L/R | DEF-foot.L/R | DEF-foot.L/R |
| Toes L/R | DEF-toe.L/R | DEF-toe.L/R |

남은 확인(헤드리스로 불가능한 것): Unity/Blender에서 열어 **변형 품질 육안 확인**(특히 슘당의 재연결 부위인 어깨·골반), Humanoid Configure에서 T-pose 확인. 레스트 포즈와 가중치를 건드리지 않았으므로 메시가 원본과 다르게 보일 구조적 이유는 없다.

주의: 새대갈의 기존 Unity 테스트 씬은 DEF- 본 이름을 그대로 참조하므로(`DEF-neck`/`DEF-head` 등) 본 이름을 유지했다. Humanoid 자동 매핑용 Unity식 이름(진돌 스타일)으로의 일괄 변경이 필요하면 같은 스크립트로 가능하다.

변환 스크립트는 `tools/rigify_deform_export.py`에 보존했다. 디자이너가 모델을 업데이트하면 재실행:

```powershell
# jobs.json: [{"src": "...원본.fbx", "dst": "...출력.fbx"}, ...] (UTF-8)
$env:BLENDER_FBX_JOBS = "경로\jobs.json"
& "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" --background --factory-startup --python tools\rigify_deform_export.py
```

## Unity 에디터 실측 검증 (2026-06-11, Unity 6000.3.10f1)

Unity 프로젝트 `2026-VOMLab-Toon-Dance`의 `[Test] Skeleton` 씬에서 자동 검증 스크립트(`Assets/Editor/DsccAvatarValidation.cs`, 메뉴 `DSCC/Validate Avatars`)로 실측했다. 결과는 `<프로젝트>/DsccValidation/report.json` + 모델별 스크린샷.

| 모델 | Humanoid Avatar | 필수 본 누락 | 키 | 레스트 포즈(팔 처짐 각) | 메시 |
|---|---|---|---|---|---|
| 진돌_유니티리그 | ✅ valid+human | 0 | 2.44m | **3.0° (T-pose)** | 정상 |
| 새대갈_deform | ✅ valid+human | 0 | 10.05m | 23.4° (약한 A-pose) | 정상 (웨이트 손실 없음) |
| 슘당_deform | ✅ valid+human | 0 | 32.62m | 22.3° (약한 A-pose) | 정상 (웨이트 손실 없음) |

확인 사항:

- 세 모델 모두 **Humanoid Avatar 생성 통과** — 매핑은 위 매핑표와 정확히 일치 (새대갈: DEF-neck/DEF-head, 슘당: Neck=DEF-spine.004, Head=DEF-spine.006).
- 진돌의 Hips가 자동 매핑에서 "Root" 본으로 잡혔던 것을 "Hips" 본으로 교정함 (`DSCC/Fix Avatar Scale And Mapping` 메뉴).
- **스케일**: 새대갈/슘당의 큰 키(10m/32.6m)는 원본 FBX와 동일함을 실측으로 확인 — 재작업이 스케일을 정확히 보존했다. 기존 새대갈 Unity 씬의 `AvatarScale: 5` 설정(사람 ~2m × 5 ≈ 10m)과 일관된 디자이너 컨벤션. 실물 크기로 맞추려면 ModelImporter Scale Factor를 조정하면 된다(새대갈 ×0.24, 슘당 ×0.075 → 약 2.4m).
- 약한 A-pose(~22°)는 Humanoid Configure의 Enforce T-Pose로 보정 가능. Animation Rigging IK 경로에는 영향 없음.
- 원본 새대갈/슘당은 import 설정을 건드리지 않았으며(Generic 유지), 예상대로 Humanoid 불가.
- 검증 인스턴스는 씬의 `DSCC_AvatarValidation` 루트 아래에 있고 **씬은 저장하지 않았다**.

## 런타임 검증 절차 (카메라 → 아바타 끝까지)

리그 정적 검증은 위로 완료. 실제 구동 검증은 Unity 프로젝트에서 다음 순서로 진행한다 (카메라 없이도 1~3단계 가능):

1. **DSCC replay 송신**: DSCC 실행 → `Start replay` (또는 `Send test frame`). 카메라 불필요.
2. **Unity Link Monitor**(핸드오프 §5.1)에서 StationId/HasPlayer/joint 수신 확인.
3. **IK Rig Setup Wizard**(§5.4)를 진돌에 적용 → replay 데이터로 손/발/머리 타깃이 움직이는지 확인. 진돌이 기준 모델, 새대갈/슘당은 재export 후 동일 절차.
4. **실 카메라 1대**: `Start Orbbec live` → 사람 1명 → 아바타 미러 방향(왼손=캐릭터 화면 기준 반대) 확인, `mirrorSkeletonX`로 교정.
5. **4대 확장**: 카메라 2대부터 순차 추가하며 스테이션별 아바타 분리 동작 확인 (`wall-a.local.json`은 4-스테이션으로 준비됨).
