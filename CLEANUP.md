# Cleanup

프로젝트 루트: `C:\WORK\SearchBook`

## 생성 범위

이 작업의 소스, 다운로드, SDK, 캐시, 임시 파일, 빌드 결과, 로그 및 테스트 데이터는 모두 프로젝트 루트 아래에 있습니다.

- `.tools\dotnet`: 휴대용 .NET 10.0.100 SDK와 런타임
- `.tools\dotnet-install.ps1`: Microsoft 공식 휴대용 SDK 설치 스크립트
- `.tools\gh\2.97.0`: GitHub 배포에만 사용하는 공식 GitHub CLI portable ZIP(전역 설치/PATH 변경 없음)
- `.tools\git\2.55.0.3`: GitHub HTTPS 푸시에만 사용하는 공식 MinGit 2.55.0.3(전역 설치/PATH 변경 없음)
- `.tools\git\MinGit-2.55.0.3-64-bit.zip`: 공식 MinGit 원본 ZIP(SHA-256 `f48e2d2dc74a24454adc6d8fd0ac25bf9c2386f19cfb06202b9465aaad4f9f05`)
- `.github-cli`: 이 프로젝트의 GitHub 배포 인증 설정(사용자 프로필 대신 프로젝트 내부에 저장, Git 제외)
- `.dotnet_cli`, `.packages`, `.tmp`, `.appdata`, `.localappdata`: 프로젝트 전용 CLI/캐시/임시 디렉터리(`.packages`에는 self-contained Windows 런타임 팩 포함)
- `bin`, `obj`, `Tests\bin`, `Tests\obj`: 빌드 결과
- `dist\SearchBook`: 사용 가능한 배포본
- `dist\SearchBook-win-x64.zip`: 복사용 배포 ZIP
- `dist\SearchBook-self-contained-win-x64`: .NET 런타임을 포함한 일반 PC용 배포본
- `dist\SearchBook-self-contained-win-x64.zip`: 일반 PC용 self-contained 배포 ZIP
- `docs\18-26.xls`: 사용자가 제공한 2026-08-06 전체 도서정보 원본(개발 원본이며 배포 ZIP에는 포함하지 않음)
- `docs\180k.xlsx` ~ `docs\250k.xlsx`: `18-26.xls`에서 생성한 번호대별 전체 도서정보 카탈로그(배포본의 `docs`에도 복사됨)
- `Assets\SearchBook-source.png`: 사용자가 제공한 고양이 아이콘 원본의 프로젝트 내 사본
- `Assets\SearchBook.png`, `Assets\SearchBook.ico`: 투명 배경으로 후처리한 앱 아이콘
- `Assets\SearchBookTray.png`, `Assets\SearchBookTray.ico`: 작은 크기에 맞춰 얼굴 중심으로 후처리한 알림 영역 아이콘
- `DesignReferences\awesome-design-md`: UI 계층과 디자인 문서 구조를 참고하기 위해 프로젝트 내부에만 얕게 복제한 MIT 저장소(약 2.8MB, 전역 설치 없음)
- `DESIGN.md`: 정제된 EMR 스타일, Windows 기본 아이콘, 조밀한 기록 화면 원칙을 정리한 프로젝트 전용 디자인 규칙
- `TestArtifacts`: 입력/XLSX/GUI 검증 자료
- `input_sample_50.txt`: 로컬 전체 도서정보에서 추린 서로 다른 등록번호 50개의 장시간 조회·애니메이션 확인용 예제
- `site-*.html`, `site-*.js`: 공개 도서관 페이지 구조 검증용 응답(쿠키 파일은 검증 후 삭제함)
- `%LocalAppData%\SearchBook\results`: 앱 실행 중 생성되는 자동저장 및 최종 결과
- `%LocalAppData%\SearchBook\results\previews`: 조회 중 `작업 중 결과 열기`를 누를 때 생성되는 잠금 충돌 방지용 Excel 스냅샷(7일 후 정리)
- `%LocalAppData%\SearchBook\updates`: 앱 또는 트레이의 업데이트 확인에서 내려받은 버전별 ZIP과 임시 `.download` 파일(실패 시 임시 파일 자동 삭제)
- `%LocalAppData%\SearchBook\diagnostics`: 등록번호와 서지정보를 제외한 사용자가 직접 저장한 진단 보고서
- `.env.example`: private GitHub 릴리즈 인증용 환경 변수 이름만 기록한 예시(실제 토큰 없음)
- `.env`: 현재 PC에서만 사용하는 private GitHub 릴리즈 토큰(평문 로컬 설정, `.gitignore`와 Release ZIP에서 제외)
- `start-searchbook.cmd`: `.env`를 읽어 해당 SearchBook 프로세스에만 토큰을 전달하는 선택적 로컬 실행기(앱도 트레이 업데이트 확인 시 `.env`를 직접 읽음)
- `build-release.ps1`: 기존 publish 폴더를 안전하게 초기화하고 `.env`가 발견되면 ZIP 생성을 중단함

## 실행 상태

- 애플리케이션 프로세스: `SearchBook.exe` (실행 중일 때만 존재하며 같은 프로세스가 알림 영역 아이콘도 소유)
- Windows 서비스, 예약 작업, 드라이버, 레지스트리 항목: 없음
- 수신 포트 또는 로컬 서버: 없음
- 영구 환경 변수 또는 PATH 변경: 없음 (`SEARCHBOOK_GITHUB_TOKEN`은 필요한 실행 프로세스에만 선택적으로 전달)
- 전역 패키지 설치: 없음
- 프로젝트 밖에 생성한 파일: 2026-08-11 테스트 당시 .NET 첫 실행이 만든
  `C:\Users\user\.dotnet\10.0.100.dotnetFirstUseSentinel`,
  `10.0.100.aspNetCertificateSentinel`, `10.0.100.toolpath.sentinel`,
  `.workloadAdvertisingManifestSentinel10.0.100` (빈 상태 표시 파일 4개, 삭제하지 않음)

## 외부 GitHub 상태

- private 저장소: `https://github.com/StandardChartered/SearchBook`
- 기본 브랜치: `main`
- 프레임워크 의존형 Release: `v1.0.0`
- Release 자산: `SearchBook-win-x64.zip` (.NET 10 Desktop Runtime x64 필요)
- 현재 로컬 소스/배포 버전: `1.3.1` (네 가지 조회 상태, 지점 포함 소장위치, 동적 버전 풋터, 결과 검토 가독성 개선 포함)

GitHub 쪽 사본까지 제거하려면 저장소 Settings의 Danger Zone에서 private 저장소를 별도로 삭제해야 합니다. 로컬 프로젝트 폴더 삭제만으로 GitHub 저장소나 Release는 삭제되지 않습니다.

빌드 중 사용한 `DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `TEMP`, `TMP`, `APPDATA`, `LOCALAPPDATA`, `DOTNET_SKIP_FIRST_TIME_EXPERIENCE`, `DOTNET_CLI_TELEMETRY_OPTOUT`는 해당 PowerShell 프로세스에만 적용되며 모두 프로젝트 내부를 가리킵니다.

## 안전한 전체 제거

1. 작업 관리자에서 `SearchBook.exe`가 실행 중이면 정상 종료합니다.
2. 보존할 `%LocalAppData%\SearchBook\results\*.xlsx` 파일을 다른 위치로 복사합니다.
3. 정확한 대상이 `C:\WORK\SearchBook`인지 확인합니다.
4. 프로젝트 폴더 전체를 삭제합니다.
5. 결과와 업데이트까지 제거하려면 `%LocalAppData%\SearchBook` 폴더도 별도로 삭제합니다.

프로젝트 폴더 삭제만으로 소스와 휴대용 SDK/캐시가 제거됩니다. 사용자 결과는 `%LocalAppData%\SearchBook`에 남으므로 보존 여부를 확인한 뒤 별도로 처리하세요.
