using System;
using System.Runtime.InteropServices;
using System.Text;
using NutLens.Core.Security;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Credentials;

namespace NutLens.Capture.Security;

/// <summary>
/// API キーを Windows 資格情報マネージャーに預ける（5.6 / ADR-0020）。
///
/// DPAPI ではなくこちらを選んだ理由は、暗号の強さではない。
/// <b>利用者が自分で見つけて消せるから。</b>
/// 「コントロール パネル → 資格情報マネージャー → Windows 資格情報」に名前で並ぶ。
/// DPAPI で暗号化したファイルを置くと、その所在を知る手段がアプリにしかなく、
/// アンインストール後に残っても消し方が無くなる。
/// </summary>
public sealed unsafe class CredentialManagerSecretStore : ISecretStore
{
    public string? TryRead(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        CREDENTIALW* credential = null;
        try
        {
            fixed (char* target = name)
            {
                if (!PInvoke.CredRead(target, CRED_TYPE.CRED_TYPE_GENERIC, 0, &credential))
                {
                    // 無い（ERROR_NOT_FOUND）は正常な状態。既定は未設定（5.6）。
                    return null;
                }
            }

            if (credential->CredentialBlob is null || credential->CredentialBlobSize == 0)
            {
                return null;
            }

            // CredentialBlobSize は<b>バイト長</b>。UTF-16 なので文字数はその半分。
            return new string(
                (char*)credential->CredentialBlob,
                0,
                (int)(credential->CredentialBlobSize / sizeof(char)));
        }
        finally
        {
            if (credential is not null)
            {
                PInvoke.CredFree(credential);
            }
        }
    }

    public void Write(string name, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        // CredentialBlobSize はバイト長（文字数ではない）。
        // 文字数を入れると鍵の末尾が切れた状態で保存され、
        // 保存も読み出しも成功したまま、Google が拒否して初めて分かる。
        // **落ちる場所と原因が離れる**ので、ここだけは取り違えない。
        byte[] blob = Encoding.Unicode.GetBytes(secret);

        fixed (char* target = name)
        fixed (byte* bytes = blob)
        {
            CREDENTIALW credential = new()
            {
                Type = CRED_TYPE.CRED_TYPE_GENERIC,
                TargetName = target,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = bytes,

                // ログオン利用者に紐づく。ドメインのローミング対象にしないため
                // CRED_PERSIST_ENTERPRISE は使わない。
                Persist = CRED_PERSIST.CRED_PERSIST_LOCAL_MACHINE,
            };

            if (!PInvoke.CredWrite(&credential, 0))
            {
                throw new InvalidOperationException(
                    $"資格情報マネージャーに保存できませんでした（Win32 エラー {Marshal.GetLastWin32Error()}）。");
            }
        }

        // 書いた直後の blob は平文の鍵。GC 任せにせずその場で消す。
        Array.Clear(blob);
    }

    public void Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        fixed (char* target = name)
        {
            // 無くても成功扱い。消す口は必ず用意する（ADR-0020 決定 5）。
            PInvoke.CredDelete(target, CRED_TYPE.CRED_TYPE_GENERIC, 0);
        }
    }
}
