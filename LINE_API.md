\# LINE Android 26.11.0 Private Client Authentication Specification

> This file contains independent reverse-engineering notes for the UpLINE project. It is not official LINE documentation, does not imply an affiliation with LINE, and may become inaccurate when LINE changes its services or applications.



\## 1. Scope



対象:



\* LINE Android `26.11.0`

\* package: `jp.naver.line.android`

\* PC向けサブ端末クライアント

\* QRコードログイン

\* PIN認証

\* セッション永続化

\* Access Token取得

\* Refresh Token取得

\* E2EE初期化

\* Talk API接続



対象外:



\* 他ユーザーのセッション取得

\* 認証回避

\* QR確認操作のバイパス

\* 既存端末からのcredential抽出



\---



\# 2. Protocol Overview



LINEの通常チャット通信は一般的なREST JSON APIではない。



中心となるのは:



```text

HTTP/HTTPS

&#x20;  ↓

LINE proprietary gateway

&#x20;  ↓

Apache Thrift

&#x20;  ↓

LINE Compact / Binary variants

&#x20;  ↓

TalkService / Auth services

```



APK内では以下を確認。



```text

AbstractLegyThriftClient

TalkServiceClient

LegacyTalkServiceClientImpl

ChatTalkServiceClient

liblegy.so

```



主要サービス:



```text

SECONDARY\_QR\_LOGIN

/acct/lgn/sq/v1



SECONDARY\_QR\_LOGIN\_PERMIT

/acct/lp/lgn/sq/v1



TALK

/S4



POLLING

/P4



Legacy TalkService

/api/v4/TalkService.do

```



\---



\# 3. Required HTTP Headers



基本:



```http

Content-Type: application/x-thrift

X-Line-Application: <client descriptor>

User-Agent: <client user agent>

```



ログイン完了後:



```http

X-Line-Access: <access token>

```



APK内で確認できるLINE固有ヘッダー:



```text

X-Line-Access

X-Line-AccessToken

X-Line-Application

X-Line-Application-Phase

X-Line-Application-Type

X-Line-Application-Version

X-Line-ChannelToken

X-Line-Mid

X-LS

X-LST

```



QR待機系では:



```http

X-Line-Access: <authSessionId>

X-LST: <long polling timeout milliseconds>

```



を使用する。



例:



```http

X-Line-Access: <authSessionId>

X-LST: 30000

```



\---



\# 4. QR Login — Recommended LINE 26.x Flow



26.xでは以下を基本フローとする。



```text

createSession

&#x20;     ↓

createQrCodeForSecure

&#x20;     ↓

QR URLへE2EE public secret追加

&#x20;     ↓

QR表示

&#x20;     ↓

checkQrCodeVerified

&#x20;     ↓

verifyCertificate

&#x20;     ↓

\[certificate invalid]

&#x20;     ↓

createPinCode

&#x20;     ↓

checkPinCodeVerified

&#x20;     ↓

qrCodeLoginV2ForSecure

&#x20;     ↓

Access Token V3

Refresh Token

Certificate

MID

&#x20;     ↓

E2EE key import / registration

```



\---



\# 5. createSession



Endpoint:



```text

/acct/lgn/sq/v1

```



RPC:



```text

createSession

```



Transport:



```text

Thrift

```



Request:



```text

empty args

```



概念IDL:



```thrift

struct CreateQrSessionRequest {

}



struct CreateQrSessionResponse {

&#x20;   1: string authSessionId

}

```



レスポンス:



```ts

{

&#x20;   authSessionId: string

}

```



この値は以降のQRログイン全体で使う一時セッションID。



例:



```text

authSessionId

&#x20;   ↓

createQrCodeForSecure

checkQrCodeVerified

verifyCertificate

createPinCode

checkPinCodeVerified

qrCodeLoginV2ForSecure

```



\---



\# 6. createSessionV2



26.11.0 APK内には別途:



```text

createSessionV2

```



も存在。



確認できたmodel:



```text

CreateSessionV2Request(mid:

CreateSessionV2Response(authSessionId:

```



したがって概念的には:



```thrift

struct CreateSessionV2Request {

&#x20;   1: string mid

}



struct CreateSessionV2Response {

&#x20;   1: string authSessionId

}

```



ただし、



```text

PC側が新規QRセッションを開始

```



する通常のsecondary-device loginでは、現在も空requestの



```text

createSession

```



を使うフローが確認されている。



`createSessionV2` はprimary側処理や別ログインコンテキスト用である可能性が高い。



Status:



```text

APK存在: CONFIRMED

通常PC QRログインに必須: NOT CONFIRMED

```



\---



\# 7. createQrCodeForSecure



LINE 26.xで最重要。



Endpoint:



```text

/acct/lgn/sq/v1

```



RPC:



```text

createQrCodeForSecure

```



Request:



```thrift

struct CreateQrCodeRequest {

&#x20;   1: string authSessionId

}

```



レスポンスはAPKと26.x解析結果から:



```thrift

struct CreateQrCodeForSecureResponse {

&#x20;   1: string callbackUrl

&#x20;   2: i32 longPollingMaxCount

&#x20;   3: i32 longPollingIntervalSec

&#x20;   4: string nonce

}

```



概念レスポンス:



```ts

interface CreateQrCodeForSecureResponse {

&#x20;   callbackUrl: string;

&#x20;   longPollingMaxCount: number;

&#x20;   longPollingIntervalSec: number;

&#x20;   nonce: string;

}

```



特に:



```text

nonce

```



を必ず保存する。



これは最終段階の:



```text

qrCodeLoginV2ForSecure

```



に返す必要がある。



\---



\# 8. QR URL Generation



サーバーが返す:



```text

callbackUrl

```



をそのままQR化するだけではE2EE移行が不完全になる場合がある。



PC側でCurve25519/X25519キーペアを生成。



```text

privateKey = random 32 bytes

publicKey = Curve25519(privateKey)

```



そのpublic keyをQR URLへ付加する。



旧来構造:



```text

?secret=<publicKey>\&e2eeVersion=1

```



最終的には:



```text

callbackUrl

\+

E2EE secret parameters

```



をQRコードとして描画する。



概念:



```ts

const keyPair = createX25519KeyPair();



const qrUrl =

&#x20;   callbackUrl +

&#x20;   `?secret=${encode(keyPair.publicKey)}\&e2eeVersion=1`;

```



注意:



実装時にはcallbackUrlに既にqueryがあるケースを考慮すること。



\---



\# 9. QR Display



生成された文字列を普通のQR Codeとして表示する。



PCクライアント:



```text

LOGIN

&#x20;┌─────────────────┐

&#x20;│                 │

&#x20;│     QR CODE     │

&#x20;│                 │

&#x20;└─────────────────┘



Scan this QR code with LINE

```



QR画像そのものをLINEサーバーから取得するAPIではない。



つまり:



```text

LINE API

&#x20;  ↓

callbackUrl文字列取得

&#x20;  ↓

PCクライアント

&#x20;  ↓

QR画像生成

```



となる。



推奨:



```text

qrcode

ZXing

qr-code-styling

```



などでローカル生成。



\---



\# 10. checkQrCodeVerified



メインスマホがQRを読み取るまで待つRPC。



Endpoint:



```text

/acct/lp/lgn/sq/v1

```



RPC:



```text

checkQrCodeVerified

```



Request:



```thrift

struct CheckQrCodeVerifiedRequest {

&#x20;   1: string authSessionId

}

```



APK:



```text

CheckQrCodeVerifiedRequest(authSessionId:

CheckQrCodeVerifiedResponse()

```



Headers:



```http

X-Line-Access: <authSessionId>

X-LST: <longPollingIntervalSec \* 1000>

```



例:



```http

X-Line-Access: abcdef...

X-LST: 30000

```



26.xではlong pollingが重要。



```ts

for (let i = 0; i < longPollingMaxCount; i++) {

&#x20;   try {

&#x20;       await checkQrCodeVerified(sessionId, {

&#x20;           timeout: longPollingIntervalSec

&#x20;       });



&#x20;       break;

&#x20;   } catch (e) {

&#x20;       if (isPollTimeout(e))

&#x20;           continue;



&#x20;       throw e;

&#x20;   }

}

```



単純な短周期poll:



```text

GET every 1 sec

```



のような実装は避ける。



\---



\# 11. verifyCertificate



以前のQRログインで保存したcertificateがある場合に使用。



Endpoint:



```text

/acct/lgn/sq/v1

```



RPC:



```text

verifyCertificate

```



Request:



```thrift

struct VerifyCertificateRequest {

&#x20;   1: string authSessionId

&#x20;   2: string certificate

}

```



概念:



```ts

await verifyCertificate({

&#x20;   authSessionId,

&#x20;   certificate: storedQrCertificate

});

```



成功:



```text

PIN確認を省略可能

```



失敗:



```text

createPinCodeへ移動

```



重要:



certificateが存在する場合は試す。



certificateが古い場合でもログイン全体を中断せず:



```text

verifyCertificate

&#x20;   ↓ fail

createPinCode

```



とする。



\---



\# 12. createPinCode



Endpoint:



```text

/acct/lgn/sq/v1

```



RPC:



```text

createPinCode

```



Request:



```thrift

struct CreatePinCodeRequest {

&#x20;   1: string authSessionId

}

```



Response:



```thrift

struct CreatePinCodeResponse {

&#x20;   1: string pinCode

}

```



APK確認:



```text

CreatePinCodeRequest(authSessionId:

CreatePinCodeResponse(pinCode:

```



UI:



```text

Verify Login



Enter this code on your primary LINE device:



483921

```



PINはログイン対象PCへ入力するのではなく、



```text

メインLINE端末で確認

```



する側の認証フローになる。



\---



\# 13. checkPinCodeVerified



Endpoint:



```text

/acct/lp/lgn/sq/v1

```



RPC:



```text

checkPinCodeVerified

```



Request:



```thrift

struct CheckPinCodeVerifiedRequest {

&#x20;   1: string authSessionId

}

```



APK確認:



```text

CheckPinCodeVerifiedRequest(authSessionId:

CheckPinCodeVerifiedResponse()

```



Headers:



```http

X-Line-Access: <authSessionId>

X-LST: <longPollingIntervalSec \* 1000>

```



これもlong polling。



\---



\# 14. qrCodeLoginV2ForSecure



LINE 26.xの最終ログインRPC。



Endpoint:



```text

/acct/lgn/sq/v1

```



RPC:



```text

qrCodeLoginV2ForSecure

```



APKで確認:



```text

QrCodeLoginV2ForSecureRequest(authSessionId:

QrCodeLoginV2Response(certificate:

qrCodeLoginV2ForSecure

```



Request field map:



```thrift

struct QrCodeLoginV2ForSecureRequest {

&#x20;   1: string authSessionId

&#x20;   2: string systemName

&#x20;   3: string modelName

&#x20;   4: bool autoLoginIsRequired

&#x20;   5: string nonce

}

```



Field IDは:



```text

1 authSessionId

2 systemName

3 modelName

4 autoLoginIsRequired

5 nonce

```



例:



```ts

{

&#x20;   authSessionId,

&#x20;   systemName: "CHROMEOS",

&#x20;   modelName: "CHROME",

&#x20;   autoLoginIsRequired: false,

&#x20;   nonce

}

```



実装上は、Talk APIで利用するWindowsの`X-Line-Application`/User-Agentとは別に、現在のセカンダリログインゲートウェイが受け付ける`CHROMEOS`/`CHROME`識別子と`autoLoginIsRequired: false`を使用する。Windows/独自モデルのままにすると、ログイン応答は返っても直後のV3 Talk APIで`V3_TOKEN_CLIENT_LOGGED_OUT`になる場合がある。

`nonce` は:



```text

createQrCodeForSecure

```



で取得した値をそのまま返す。



\---



\# 15. qrCodeLoginV2 Response



26.x解析結果とAPKの構造から:



```thrift

struct QrCodeLoginV2Response {

&#x20;   1: string certificate

&#x20;   2: string accessTokenV2

&#x20;   3: TokenV3IssueResult tokenV3IssueResult

&#x20;   4: string mid

&#x20;   5: i64 lastBindTimestamp

&#x20;   6: map<string,string> metaData



&#x20;   // compatibility fields may exist

}

```



最重要なのは:



```text

field 1

certificate



field 3

TokenV3IssueResult



field 4

MID



field 6

metadata

```



\---



\# 16. TokenV3IssueResult



概念:



```ts

interface TokenV3IssueResult {

&#x20;   accessToken: string;

&#x20;   refreshToken: string;

&#x20;   expirationTimeSec: number;

&#x20;   ...

&#x20;   issuedTimeSec: number;

}

```



実装ではfield IDベースで扱う。



現在の実装解析では:



```text

tokenInfo\[1] = accessToken

tokenInfo\[2] = refreshToken

tokenInfo\[3] = expiration

tokenInfo\[6] = issued timestamp/base time

```



保存:



```text

accessToken

refreshToken

expire

```



\---



\# 17. Authentication State



ログイン前:



```text

authSessionId

```



ログイン中:



```http

X-Line-Access: <authSessionId>

```



ログイン後:



```text

accessToken

```



へ切り替える。



```http

X-Line-Access: <accessToken>

```



つまり:



```text

authSessionId != Access Token

```



必ず別物として扱う。



\---



\# 18. Certificate Storage



QRログイン成功時:



```text

QrCodeLoginV2Response.certificate

```



を保存。



例:



```text

app-data/

&#x20; auth/

&#x20;   qr-certificate

```



次回ログイン:



```text

createSession

→ createQrCodeForSecure

→ QR scan

→ verifyCertificate

```



certificate有効:



```text

PIN省略

```



certificate無効:



```text

PIN認証

```



\---



\# 19. Token Storage



保存対象:



```json

{

&#x20; "accessToken": "...",

&#x20; "refreshToken": "...",

&#x20; "certificate": "...",

&#x20; "mid": "U...",

&#x20; "expiresAt": 0

}

```



Windowsではplaintext JSONを避ける。



推奨:



```text

Windows Credential Manager

DPAPI

```



Electronなら:



```text

safeStorage

```



Tauriなら:



```text

Windows Credential Manager

```



またはOS keychain。



\---



\# 20. Access Token Header



通常RPC:



```http

POST /S4



Content-Type: application/x-thrift

X-Line-Application: ...

X-Line-Access: <accessToken>

```



\---



\# 21. Initial Authentication Validation



ログイン完了後、まず:



```text

getProfile

```



を実行する。



成功すれば:



```text

access token valid

```



と判断。



概念:



```text

login

&#x20; ↓

save credentials

&#x20; ↓

getProfile()

&#x20; ↓

READY

```



\---



\# 22. Application State Machine



```text

LOGGED\_OUT



&#x20;↓ createSession



SESSION\_CREATED



&#x20;↓ createQrCodeForSecure



QR\_CREATED



&#x20;↓ display QR



WAITING\_QR\_SCAN



&#x20;↓ checkQrCodeVerified



QR\_VERIFIED



&#x20;↓ verifyCertificate



&#x20;┌───────────── success

&#x20;│

&#x20;│     ↓

&#x20;│ LOGIN\_READY

&#x20;│

&#x20;└───────────── failure

&#x20;      ↓

&#x20;CREATE\_PIN



&#x20;      ↓

&#x20;WAITING\_PIN



&#x20;      ↓

&#x20;checkPinCodeVerified



&#x20;      ↓

&#x20;LOGIN\_READY



&#x20;↓ qrCodeLoginV2ForSecure



TOKEN\_RECEIVED



&#x20;↓ initialize E2EE



AUTHENTICATED



&#x20;↓ getProfile



READY

```



\---



\# 23. Suggested PC Login API



UIとは分離して:



```ts

interface LineAuth {

&#x20;   startQrLogin(): Promise<QrLoginSession>;



&#x20;   waitForQrScan(

&#x20;       session: QrLoginSession

&#x20;   ): Promise<void>;



&#x20;   verifySavedCertificate(

&#x20;       session: QrLoginSession

&#x20;   ): Promise<boolean>;



&#x20;   createPinCode(

&#x20;       session: QrLoginSession

&#x20;   ): Promise<string>;



&#x20;   waitForPinVerification(

&#x20;       session: QrLoginSession

&#x20;   ): Promise<void>;



&#x20;   completeQrLogin(

&#x20;       session: QrLoginSession

&#x20;   ): Promise<AuthCredentials>;



&#x20;   restoreSession(): Promise<AuthCredentials | null>;



&#x20;   logout(): Promise<void>;

}

```



\---



\# 24. QrLoginSession



```ts

interface QrLoginSession {

&#x20;   authSessionId: string;



&#x20;   callbackUrl: string;



&#x20;   qrUrl: string;



&#x20;   nonce: string;



&#x20;   longPollingMaxCount: number;



&#x20;   longPollingIntervalSec: number;



&#x20;   e2ee: {

&#x20;       privateKey: Uint8Array;

&#x20;       publicKey: Uint8Array;

&#x20;   };

}

```



\---



\# 25. AuthCredentials



```ts

interface AuthCredentials {

&#x20;   mid: string;



&#x20;   accessToken: string;



&#x20;   refreshToken?: string;



&#x20;   certificate?: string;



&#x20;   expiresAt?: number;

}

```



\---



\# 26. startQrLogin()



Pseudo implementation:



```ts

async function startQrLogin() {

&#x20;   const session =

&#x20;       await rpc.createSession();



&#x20;   const qr =

&#x20;       await rpc.createQrCodeForSecure(

&#x20;           session.authSessionId

&#x20;       );



&#x20;   const keypair =

&#x20;       generateX25519KeyPair();



&#x20;   const qrUrl =

&#x20;       appendE2EESecret(

&#x20;           qr.callbackUrl,

&#x20;           keypair.publicKey

&#x20;       );



&#x20;   return {

&#x20;       authSessionId:

&#x20;           session.authSessionId,



&#x20;       callbackUrl:

&#x20;           qr.callbackUrl,



&#x20;       qrUrl,



&#x20;       nonce:

&#x20;           qr.nonce,



&#x20;       longPollingMaxCount:

&#x20;           qr.longPollingMaxCount,



&#x20;       longPollingIntervalSec:

&#x20;           qr.longPollingIntervalSec,



&#x20;       e2ee: keypair

&#x20;   };

}

```



\---



\# 27. completeQrLogin()



```ts

async function completeQrLogin(session) {

&#x20;   const response =

&#x20;       await rpc.qrCodeLoginV2ForSecure({

&#x20;           authSessionId:

&#x20;               session.authSessionId,



&#x20;           systemName:

&#x20;               "CHROMEOS",



&#x20;           modelName:

&#x20;               "CHROME",



&#x20;           autoLoginIsRequired:

&#x20;               false,



&#x20;           nonce:

&#x20;               session.nonce

&#x20;       });



&#x20;   const token =

&#x20;       response.tokenV3IssueResult;



&#x20;   await credentialStore.save({

&#x20;       accessToken:

&#x20;           token.accessToken,



&#x20;       refreshToken:

&#x20;           token.refreshToken,



&#x20;       certificate:

&#x20;           response.certificate,



&#x20;       mid:

&#x20;           response.mid

&#x20;   });



&#x20;   return credentials;

}

```



\---



\# 28. E2EE After Login



これは重要。



LINEはLetter Sealingを使用するため、



```text

QRログイン成功

```



だけでチャットクライアントとして完成ではない。



QR生成時に作成した:



```text

X25519 private key

```



とサーバーから返されたE2EE metadataを使用して:



```text

existing E2EE keys import

```



を行う。



ForSecureレスポンスではE2EE情報が:



```text

metaData\["e2eeInfo"]

```



に入る場合がある。



概念:



```ts

const e2eeInfo =

&#x20;   response.metaData?.e2eeInfo;



if (e2eeInfo) {

&#x20;   importTransferredE2EEKeys(

&#x20;       e2eeInfo,

&#x20;       session.e2ee.privateKey

&#x20;   );

}

```



失敗・未提供の場合:



```text

registerE2EEKeyPair()

```



へフォールバック。



\---



\# 29. Legacy QR Flow



APKには旧APIも残っている。



```text

createSession

createQrCode

checkQrCodeVerified

verifyCertificate

createPinCode

checkPinCodeVerified

qrCodeLogin

qrCodeLoginV2

```



旧:



```thrift

struct QrCodeLoginRequest {

&#x20;   1: string authSessionId

&#x20;   2: string systemName

&#x20;   3: bool autoLoginIsRequired

}

```



V2:



```thrift

struct QrCodeLoginV2Request {

&#x20;   1: string authSessionId

&#x20;   2: string systemName

&#x20;   3: string modelName

&#x20;   4: bool autoLoginIsRequired

}

```



ただしLINE Android 26.xでは:



```text

createQrCodeForSecure

qrCodeLoginV2ForSecure

```



を優先する。



\---



\# 30. Login-related RPC Inventory Found in 26.11.0



APKから確認できたもの:



```text

createSession

createSessionV2



createQrCode

createQrCodeForSecure



checkQrCodeVerified



verifyCertificate



createPinCode

checkPinCodeVerified

cancelPinCode



qrCodeLoginV2

qrCodeLoginV2ForSecure



verifyQrCode



verifyPinCode



existPinCode



fetchPhonePinCodeMsg



requestToSendPhonePinCode



verifyPhonePinCode



checkIfPhonePinCodeMsgVerified

```



その他account migration関連:



```text

migratePrimaryUsingQrCode

```



PAaK/passkey系:



```text

AuthenticateWithPaak

CancelPaakAuthentication

GetChallengeForPaakAuth

```



PCの通常QRログインで最低限必要なのは:



```text

createSession

createQrCodeForSecure

checkQrCodeVerified

verifyCertificate

createPinCode

checkPinCodeVerified

qrCodeLoginV2ForSecure

```



\---



\# 31. Error Model



APK:



```text

SecondaryQrCodeException(code:

```



旧IDLから確認されている代表値:



```text

0 INTERNAL\_ERROR

1 ILLEGAL\_ARGUMENT

2 VERIFICATION\_FAILED

3 NOT\_ALLOWED\_QR\_CODE\_LOGIN

4 VERIFICATION\_NOTICE\_FAILED

5 RETRY\_LATER

100 INVALID\_CONTEXT

101 APP\_UPGRADE\_REQUIRED

```



実装ではerror codeを直接UIへ出さず:



```ts

switch (code) {

&#x20;   case RETRY\_LATER:

&#x20;       retry();

&#x20;       break;



&#x20;   case VERIFICATION\_FAILED:

&#x20;       restartLogin();

&#x20;       break;



&#x20;   case APP\_UPGRADE\_REQUIRED:

&#x20;       showUnsupportedProtocol();

&#x20;       break;

}

```



とする。



\---



\# 32. QR Expiration



QRセッションは永続ではない。



以下を同じsessionで再利用しない:



```text

expired authSessionId

expired callbackUrl

expired nonce

```



期限切れ時:



```text

createSession()

```



から完全にやり直す。



\---



\# 33. Security Requirements



絶対にログへ出さない:



```text

accessToken

refreshToken

certificate

X25519 private key

E2EE key chain

```



development logでも:



```text

authSessionId = abcd...1234

accessToken = eyJh...xxxx

```



のようにmaskする。



\---



\# 34. Recommended Internal Modules



```text

src/

&#x20; line/



&#x20;   transport/

&#x20;     thrift.ts

&#x20;     http.ts

&#x20;     legy.ts



&#x20;   auth/

&#x20;     qr.ts

&#x20;     certificate.ts

&#x20;     token.ts

&#x20;     credential-store.ts



&#x20;   e2ee/

&#x20;     x25519.ts

&#x20;     key-transfer.ts

&#x20;     letter-sealing.ts



&#x20;   talk/

&#x20;     client.ts

&#x20;     messages.ts

&#x20;     contacts.ts

&#x20;     chats.ts



&#x20;   polling/

&#x20;     client.ts



&#x20;   media/

&#x20;     obs.ts

&#x20;     upload.ts

```



\---



\# 35. Architecture



```text

┌──────────────────────────────┐

│            UI                │

│ Discord-like desktop UI      │

└──────────────┬───────────────┘

&#x20;              │

&#x20;              ▼

┌──────────────────────────────┐

│       Client Controller      │

└──────────────┬───────────────┘

&#x20;              │

&#x20;      ┌───────┴───────┐

&#x20;      ▼               ▼

┌────────────┐   ┌─────────────┐

│ Auth       │   │ Talk        │

│ QR Login   │   │ Messaging   │

└─────┬──────┘   └──────┬──────┘

&#x20;     │                 │

&#x20;     ▼                 ▼

┌──────────────────────────────┐

│        Thrift Layer          │

└──────────────┬───────────────┘

&#x20;              │

&#x20;              ▼

┌──────────────────────────────┐

│ LINE endpoints               │

│ /acct/lgn/sq/v1              │

│ /acct/lp/lgn/sq/v1           │

│ /S4                          │

│ /P4                          │

└──────────────────────────────┘

```



\---



\# 36. Minimum Viable Login Implementation



Phase 1:



```text

\[✓] Thrift encoder

\[✓] Thrift decoder

\[ ] createSession

\[ ] createQrCodeForSecure

\[ ] QR rendering

\[ ] checkQrCodeVerified

\[ ] certificate handling

\[ ] PIN handling

\[ ] qrCodeLoginV2ForSecure

\[ ] credential storage

\[ ] getProfile

```



Phase 2:



```text

\[ ] E2EE key transfer

\[ ] Letter Sealing

\[ ] refresh token

\[ ] reconnect

```



Phase 3:



```text

\[ ] /S4 TalkService

\[ ] /P4 / LEGY event receiving

\[ ] chat list

\[ ] message history

\[ ] send text

```



Phase 4:



```text

\[ ] /obs media upload

\[ ] image send

\[ ] video/file send

\[ ] thumbnails

```



\---



\# 37. Confidence Table



| Item                               | Status                                         |

| ---------------------------------- | ---------------------------------------------- |

| `/acct/lgn/sq/v1`                  | CONFIRMED IN 26.11.0 APK                       |

| `/acct/lp/lgn/sq/v1`               | CONFIRMED IN 26.11.0 APK                       |

| `/S4`                              | CONFIRMED                                      |

| `/P4`                              | CONFIRMED                                      |

| `createSession`                    | CONFIRMED                                      |

| `createSessionV2`                  | CONFIRMED                                      |

| `createQrCodeForSecure`            | CONFIRMED                                      |

| `checkQrCodeVerified`              | CONFIRMED                                      |

| `createPinCode`                    | CONFIRMED                                      |

| `checkPinCodeVerified`             | CONFIRMED                                      |

| `qrCodeLoginV2ForSecure`           | CONFIRMED                                      |

| ForSecure `nonce`                  | CONFIRMED                                      |

| longPollingMaxCount                | HIGH CONFIDENCE                                |

| longPollingIntervalSec             | HIGH CONFIDENCE                                |

| X25519 QR secret                   | CONFIRMED / HISTORIC + CURRENT IMPLEMENTATIONS |

| `X-Line-Access`                    | CONFIRMED IN APK                               |

| `X-Line-Application`               | CONFIRMED IN APK                               |

| exact production hostname          | DYNAMIC / NOT YET LOCKED                       |

| full Letter Sealing implementation | NEXT ANALYSIS TARGET                           |

| current OBS image upload flow      | NEXT ANALYSIS TARGET                           |



\---



\# 38. Recommended Flow for the Desktop Client



実際のPC版ではこれだけを入口にする。



```ts

const session = await line.auth.beginQrLogin();



ui.showQr(session.qrUrl);



await line.auth.waitForQrScan(session);



if (

&#x20;   !await line.auth.tryCertificate(session)

) {

&#x20;   const pin =

&#x20;       await line.auth.createPin(session);



&#x20;   ui.showPin(pin);



&#x20;   await line.auth.waitForPin(session);

}



const account =

&#x20;   await line.auth.finish(session);



await line.e2ee.initialize(account);



await line.connect();



ui.openMainScreen();

```



これをUI側から見た唯一のログインフローとする。
