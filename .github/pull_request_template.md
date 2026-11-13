<!--
  ほとんどの変更に ADR は要りません。
  下の確認は「書かせる」ためではなく「気づいてもらう」ためのものです。
  分からない項目は空のままで構いません。こちらで確かめます。

  Most changes need no ADR. The checks below exist to raise a question,
  not to create paperwork. Leave anything you are unsure about blank —
  the maintainer will check.
-->

## 何をしましたか / What does this change

<!-- 1〜2 行で。/ One or two lines. -->

## なぜ / Why

<!-- 直した不具合、困っていたこと。Issue があれば番号を。 -->
<!-- The bug you hit, or what was inconvenient. Link an issue if there is one. -->

---

## 確認 / Checks

- [ ] `dotnet build NutLens.slnx -c Debug` が**警告ゼロ**で通る / builds with **no warnings**
- [ ] `dotnet test` が通る / tests pass

**既存の決定に触れますか / Does this change a decision in `docs/adr/`?**

- [ ] いいえ、または分からない（**そのままで構いません**） / No, or not sure — **that is fine**
- [ ] はい（**どの ADR か**を下に書いてください。ADR は維持者が書きます） / Yes — say which below; the maintainer writes the ADR

<!-- 「はい」の場合：どの ADR の、どの決定ですか。なぜ成り立たなくなりましたか。 -->
<!-- If yes: which ADR and which decision, and why it no longer holds. -->

**設計書 / Design documents**

- [ ] 直した / updated
- [ ] 直していない（**維持者が直します**） / not updated — **the maintainer will handle it**

**次を触りましたか / Did you touch any of these?**（触っていなければ飛ばしてください / skip if not）

- [ ] 録音（NAudio）→ `dotnet run --project tools/NutLens.CaptureProbe -- 3 --audio-idle` を走らせた
- [ ] 資格情報マネージャー → `--secret-selftest` を走らせた
- [ ] ホットキー → `--hotkey-selftest` を走らせた

<!--
  これらは単体テストで守れません（テストは net10.0 で、Windows 専用の
  プロジェクトを参照できないため）。録音は実際に 1.2 GB の WAV を
  作ったことがあり、この確認が唯一の歯止めです。

  Unit tests cannot cover these. For the recorder this self-check is the only guard —
  a 1.2 GB WAV really did get produced once.
-->
