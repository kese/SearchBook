# Cleanup

프로젝트 루트: `C:\WORK\SearchBook`

## 생성 범위

이 작업의 소스, 다운로드, SDK, 캐시, 임시 파일, 빌드 결과, 로그 및 테스트 데이터는 모두 프로젝트 루트 아래에 있습니다.

- `.tools\dotnet`: 휴대용 .NET 10.0.100 SDK와 런타임
- `.tools\dotnet-install.ps1`: Microsoft 공식 휴대용 SDK 설치 스크립트
- `.dotnet_cli`, `.packages`, `.tmp`, `.appdata`, `.localappdata`: 프로젝트 전용 CLI/캐시/임시 디렉터리(`.packages`에는 self-contained Windows 런타임 팩 포함)
- `bin`, `obj`, `Tests\bin`, `Tests\obj`: 빌드 결과
- `dist\SearchBook`: 사용 가능한 배포본
- `dist\SearchBook-win-x64.zip`: 복사용 배포 ZIP
- `dist\SearchBook-self-contained-win-x64`: .NET 런타임을 포함한 일반 PC용 배포본
- `dist\SearchBook-self-contained-win-x64.zip`: 일반 PC용 self-contained 배포 ZIP
- `TestArtifacts`: 입력/XLSX/GUI 검증 자료
- `site-*.html`, `site-*.js`: 공개 도서관 페이지 구조 검증용 응답(쿠키 파일은 검증 후 삭제함)
- `results`: 앱 실행 중 생성되는 자동저장 및 최종 결과

## 실행 상태

- 애플리케이션 프로세스: `SearchBook.exe` (실행 중일 때만 존재)
- Windows 서비스, 예약 작업, 드라이버, 레지스트리 항목: 없음
- 수신 포트 또는 로컬 서버: 없음
- 영구 환경 변수 또는 PATH 변경: 없음
- 전역 패키지 설치: 없음
- 프로젝트 밖에 생성한 파일: 없음

빌드 중 사용한 `DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `TEMP`, `TMP`, `APPDATA`, `LOCALAPPDATA`, `DOTNET_SKIP_FIRST_TIME_EXPERIENCE`, `DOTNET_CLI_TELEMETRY_OPTOUT`는 해당 PowerShell 프로세스에만 적용되며 모두 프로젝트 내부를 가리킵니다.

## 안전한 전체 제거

1. 작업 관리자에서 `SearchBook.exe`가 실행 중이면 정상 종료합니다.
2. 보존할 `results\*.xlsx` 파일을 다른 위치로 복사합니다.
3. 정확한 대상이 `C:\WORK\SearchBook`인지 확인합니다.
4. 프로젝트 폴더 전체를 삭제합니다.

프로젝트 폴더 삭제만으로 작업 파일과 휴대용 SDK/캐시가 모두 제거됩니다. 기존 사용자 프로그램이나 데이터는 제거하지 마세요.
