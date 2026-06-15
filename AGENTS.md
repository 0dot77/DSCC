# DSCC Coding Hook

When editing DSCC body tracking, Orbbec/K4A wrapper integration, Unity skeleton receiving, coordinate mapping, retargeting, IK, head tracking, debug skeleton drawing, or avatar calibration, read this first:

- `spec/Orbbec_Unity_Retargeting_Research.md`

Use that document as the decision record for coordinate spaces, joint confidence handling, per-segment scaling, and Unity Humanoid/direct-bone retargeting tradeoffs.

Do not use one global avatar scale to solve stylized character proportions unless the change explicitly explains why head/neck/debug scale are unaffected.

Before finalizing retargeting changes, state:

- source coordinate space and destination coordinate space;
- scale used for body, head, and debug drawing;
- low-confidence joint behavior;
- Unity console compile result if Unity files were touched.
