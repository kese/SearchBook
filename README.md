# SearchBook

한국교통대학교 도서관의 EM/WM 등록번호를 일괄 조회해 Excel로 정리하는 Windows GUI 프로그램입니다.

## 왜 C#/.NET WPF인가

- Python과 패키지를 별도로 설치할 필요 없이 Windows 실행 파일로 배포할 수 있습니다.
- 파일 드래그앤드롭, 취소, 진행률, 고해상도 화면을 Windows 네이티브 UI로 처리합니다.
- 외부 Excel 라이브러리 없이 `.xlsx`를 직접 읽고 써 의존성을 줄였습니다.

## 주요 기능

- `.xlsx`, `.txt`, `.csv`, `.tsv` 파일 드래그앤드롭
- 파일 전체에서 EM/WM 등록번호 자동 추출 및 중복 제거
- 등록번호별 도서상태, 반납기한, 소장위치, 청구기호, 서명, 상세 URL 수집
- 서버 오류 재시도, 요청 간격 설정, 진행률과 남은 시간 표시
- 50건마다 자동저장, 사용자 중지 시 처리 결과 보존
- 필터와 고정 헤더가 포함된 Excel 결과 생성

## 실행

일반 PC에는 `dist\SearchBook-self-contained-win-x64.zip`을 사용합니다. ZIP을 완전히 압축 해제한 뒤 `SearchBook.exe`를 더블클릭하면 별도의 .NET 설치 없이 실행됩니다.

`dist\SearchBook`과 `SearchBook-win-x64.zip`은 용량이 작은 프레임워크 의존형 배포본이며, 대상 PC에 .NET 10 Desktop Runtime x64가 필요합니다.

1. EM/WM 등록번호가 들어 있는 파일을 창으로 끌어 놓습니다.
2. 처음에는 작은 목록으로 시험합니다.
3. `조회 시작`을 누릅니다.
4. 완료 결과는 실행 파일 옆 `results` 폴더에 자동 저장됩니다.

명령줄에서 입력 파일을 함께 넘겨도 즉시 불러옵니다.

```powershell
.\SearchBook.exe "C:\path\등록번호.xlsx"
```

## 입력 형식

등록번호가 어느 열에 있든 `EM` 또는 `WM`으로 시작하는 값을 찾습니다. 대소문자는 구분하지 않으며 중복은 첫 항목만 남깁니다.

## 빌드와 테스트

모든 SDK, 캐시, 임시 파일은 프로젝트 폴더 안에 둡니다.

```powershell
.\build-release.ps1
.\build-release.ps1 -SelfContained
```

테스트는 저장된 실제 응답 파싱, TXT 입력, XLSX 왕복을 검사합니다. `--live`를 붙이면 공개 등록번호 한 건을 현재 서버에서 실제 조회합니다.

```powershell
.\.tools\dotnet\dotnet.exe run --project .\Tests\SearchBook.Tests.csproj -c Release --no-restore
```

## 주의

- 도서관 서버의 HTML 구조가 바뀌면 파서를 조정해야 할 수 있습니다.
- 1만 건은 검색과 소장정보 요청이 각각 필요해 수 시간이 걸릴 수 있습니다.
- 기본 0.7초 간격을 권장합니다. 서버가 느리거나 오류가 증가하면 1.0~1.5초로 늘리세요.
- 로그인이나 개인정보는 사용하지 않습니다.
