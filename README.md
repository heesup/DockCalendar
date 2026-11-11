# Dock Calendar

PowerToys Command Palette Dock에 날짜 밴드를 추가하고, 클릭하면 Windows 11 스타일의 캘린더 플라이아웃을 띄우는 확장입니다. Google 캘린더 일정을 읽어 와서 월간 · 주간 · 일간으로 보여 줍니다.

- **월간**: 시계, 미니 달력(일정이 있는 날은 색 점), 선택한 날부터 7일간의 일정 목록
- **주간 / 일간**: 왼쪽 패널은 그대로 두고 오른쪽에 시간 그리드가 펼쳐집니다. 종일 · 여러 날 일정은 위쪽 띠에, 겹치는 일정은 나란히 표시됩니다.
- Dock 밴드에는 오늘 날짜와 다음 일정이 표시됩니다.
- Windows의 라이트/다크 모드와 강조색을 따르고, 시스템 언어가 한국어면 한국어로, 아니면 영어로 표시됩니다.
- 일정은 **읽기 전용**입니다. 일정을 클릭하면 Google 캘린더 웹에서 열립니다.

## 스크린샷

데모 데이터로 찍은 화면입니다.

| 월간 | 일간 |
|---|---|
| ![월간 보기: 미니 달력과 일정 목록](screenshots/month.png) | ![일간 보기: 왼쪽 패널과 하루 시간 그리드](screenshots/day.png) |

![주간 보기: 왼쪽 패널과 일주일 시간 그리드](screenshots/week.png)

## 설치

필요한 것: Windows 11, Dock을 켠 PowerToys Command Palette 0.9 이상, .NET 10 SDK(`10.0.401` 이상).

```powershell
./build-and-install.ps1
```

테스트 → 게시 → 개발 모드 패키지 등록 → Command Palette 재시작까지 한 번에 합니다. 그다음 Command Palette **Settings → Dock → Bands**에서 **Dock Calendar** 밴드를 추가하세요. 확장, 명령, 밴드 모두 같은 이름입니다. 확장을 켜는 것만으로는 밴드가 Dock에 올라가지 않습니다.

빌드와 테스트만 하려면 `./build.ps1`.

## Google 캘린더 연결

Google은 앱마다 OAuth 클라이언트를 요구하므로, 본인 Google Cloud 프로젝트에서 클라이언트를 한 번 만들어야 합니다(무료).

1. [Google Cloud Console](https://console.cloud.google.com/)에서 프로젝트를 만듭니다.
2. **API 및 서비스 → 라이브러리**에서 **Google Calendar API**를 사용 설정합니다.
3. **OAuth 동의 화면**을 구성합니다(사용자 유형: 외부). 게시 상태가 **테스트**이면 본인 계정을 테스트 사용자로 추가해야 하고, 로그인이 **7일마다 만료**됩니다. 계속 쓰려면 **프로덕션으로 게시**하세요. 본인만 쓰는 앱은 인증 심사 없이도 "확인되지 않은 앱" 경고를 넘기고 사용할 수 있습니다.
4. **사용자 인증 정보 → 사용자 인증 정보 만들기 → OAuth 클라이언트 ID**에서 애플리케이션 유형을 **데스크톱 앱**으로 만듭니다.
5. 플라이아웃 오른쪽 위 톱니바퀴를 눌러 **클라이언트 ID**와 **클라이언트 보안 비밀번호**를 붙여 넣고 **Google로 로그인**을 누릅니다. 브라우저에서 동의하면 연결됩니다.

요청하는 권한은 `calendar.readonly` 하나뿐입니다. 설정의 **표시할 캘린더**에서 캘린더별로 켜고 끌 수 있고, 기본값은 Google 캘린더에서 체크해 둔 캘린더입니다.

## 조작

| 동작 | 방법 |
|---|---|
| 보기 전환 | 월 / 주 / 일 버튼, 또는 `M` `W` `D` |
| 날짜 이동 | 달력에서 날짜 클릭, `←` `→`(하루), `↑` `↓`(일주일), `PageUp` `PageDown`(한 달) |
| 달 넘기기 | 달력 위의 ∧ ∨ 버튼, 또는 달력 위에서 마우스 휠 |
| 오늘로 | 시계나 달 제목 클릭, `T` 또는 `Home` |
| 지금 동기화 | 새로 고침 버튼 또는 `F5` |
| 닫기 | `Esc`, 창 밖 클릭, 또는 Dock 밴드 다시 클릭 |

일정은 10분마다, 그리고 플라이아웃을 열 때(마지막 동기화가 5분 넘게 지났으면) 다시 읽습니다.

## Command Palette 없이 실행해 보기

```powershell
dotnet run --project DockCalendar -c Release -- --demo --pin --view=week
```

- `--demo` Google 계정 없이 샘플 일정으로 표시
- `--pin` 포커스를 잃어도 닫히지 않음
- `--view=month|week|day` 시작할 보기

## 저장되는 데이터

모두 `%LocalAppData%\DockCalendar`에 있습니다(패키지로 설치하면 Windows가 패키지 전용 폴더로 리디렉션합니다).

- `settings.json`: 클라이언트 ID, 보기 설정, 그리고 **DPAPI로 암호화된** 클라이언트 보안 비밀번호와 갱신 토큰. 암호화는 현재 Windows 사용자에게 묶여 있어 파일만 복사해서는 쓸 수 없습니다.
- `cache.json`: 마지막으로 읽은 일정. 플라이아웃을 열자마자 그릴 수 있게 하는 용도이며 로그아웃하면 지워집니다.

액세스 토큰은 메모리에만 둡니다.

## 구조

```text
DockCalendar/
  Calendar/   일정 모델, 캐시 저장소, 시간 그리드 배치 계산, 데모 데이터
  Google/     OAuth 로그인(PKCE + 루프백)과 Calendar v3 클라이언트
  Ui/         플라이아웃 창(WPF), 테마, Win32 연동
  DockCalendarCommandsProvider.cs   Dock 밴드와 Command Palette 명령
DockCalendar.Tests/
  배치 계산, Google 응답 파싱, 저장소 · 설정 테스트
tools/make-icons.ps1   패키지 아이콘 생성
```

## 알아 둘 점

- 플라이아웃은 Command Palette 화면이 아니라 확장 프로세스가 직접 띄우는 창입니다. Command Palette의 상세 페이지는 Adaptive Card라서 달력 그리드나 반투명 배경을 그릴 수 없기 때문입니다.
- 창은 Dock이 있는 화면 가장자리(마우스 포인터 위치 기준) 옆에 뜹니다. 가장자리가 아닌 곳에서 열면 작업 영역 오른쪽 아래에 뜹니다.
- 밴드 글자는 제자리에서 갱신합니다. `ItemsChanged`를 올리면 Command Palette가 Dock 전체를 다시 만들어 다른 밴드가 초기화됩니다.
- 아크릴 배경은 Windows 11 22H2 이상에서만 그려집니다. 그보다 낮은 버전에서는 반투명 대신 어두운 단색 배경으로 보입니다.
