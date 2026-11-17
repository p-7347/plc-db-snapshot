# PLC DB Snapshot

S7-1500 / S7-1200 PLC에 랜선으로 직접 붙어서 **DB 목록 확인 → 현재값 저장(스냅샷) → 비교**를 하는 읽기 전용 Windows 프로그램입니다.
TIA Portal, 라이선스, 프로젝트 파일, 인터넷이 없어도 동작합니다.

Read-only Windows tool that connects straight to an S7-1500/1200 CPU (S7CommPlus, TCP 102), lists its DBs,
saves their live values to a CSV snapshot and compares snapshots. No TIA Portal needed.

> **읽기 전용입니다.** 이 프로그램에는 PLC에 값을 쓰는 기능이 없습니다 (복원 기능 없음).

## 할 수 있는 것

1. **DB 목록 보기**: 접속하면 PLC에 있는 DB 전체(번호, 이름, 태그 수)와 I/Q/M 영역이 나옵니다. 최적화 DB, 인스턴스 DB, F-DB 모두 심볼 이름으로 읽습니다.
2. **현재값 저장**: 원하는 DB를 체크하고 저장하면 `태그, 형식, 값, 상태`가 CSV로 저장됩니다. 엑셀에서 바로 열립니다. 파일 맨 위에 PLC 주소, 시각, 메모가 같이 기록됩니다.
3. **비교**
   - 저장본 A ↔ 저장본 B
   - 저장본 A ↔ **지금 PLC 값** (A에 있는 태그만 다시 읽어서 비교, 결과를 새 스냅샷으로 저장 가능)
   - 결과: `변경`, `A에만 있음`, `B에만 있음`, `읽기 오류`. 결과 검색, CSV 저장 가능
   - 옵션: 제외할 태그 정규식 (예: `\.ET$|Heartbeat`), Real 허용 오차, 같은 값도 표시

## 사용법 (현장)

1. 노트북을 PLC와 같은 네트워크에 연결 (PG 포트 등, TCP 102 통신 가능해야 함)
2. `PlcDbSnapshot.exe` 실행 → PLC IP 입력 → **접속**
   - 처음 접속할 때 PLC 전체 심볼을 한 번 읽어서 프로그램이 크면 1~2분 걸릴 수 있습니다.
   - PLC에 접근 보호 비밀번호가 걸려 있으면 비밀번호 칸에 입력합니다.
3. **1. 현재값 저장** 탭: DB 검색/체크 → 메모 입력(선택) → **선택한 DB 현재값 저장**
   - 기본 저장 위치: `문서\PlcDbSnapshot\`
4. **2. 비교** 탭: A 파일 열기 → B 파일 열기 또는 `B = 지금 PLC 값` 선택 → **비교**

## 설치

- Windows 10/11 x64, .NET Framework 4.8 (Windows 10/11에 기본 포함)
- 빌드된 폴더(`PlcDbSnapshot.exe`, `S7CommPlusDriver.dll`, `PlcDbSnapshot.Core.dll`, `libssl-3-x64.dll`, `libcrypto-3-x64.dll`)를 통째로 복사해서 실행
- GitHub Actions 빌드 결과(`PlcDbSnapshot` 아티팩트)를 내려받아도 됩니다.

## 지원 PLC / 제약

- S7-1500 펌웨어 V2.9 이상, S7-1200 펌웨어 V4.3 이상 (TLS 보안 통신 지원 CPU), TIA V17 이상으로 구성된 프로젝트
- 사용하는 통신 드라이버([S7CommPlusDriver](https://github.com/thomas-v2/S7CommPlusDriver))는 Siemens 공식이 아닌 커뮤니티(리버스 엔지니어링) 구현입니다. 읽기만 하지만, 처음에는 PLCSIM이나 정지 중인 설비에서 먼저 확인하세요.
- 드라이버가 지원하지 않는 일부 형식은 `지원 안 되는 형식`으로 표시되고 값은 비어 있습니다.
- 스냅샷은 여러 번의 읽기 요청으로 나눠 읽기 때문에, 설비가 돌고 있으면 DB 간 값이 완전히 같은 순간의 값은 아닙니다.

## 빌드

```
dotnet build src/PlcDbSnapshot.App/PlcDbSnapshot.App.csproj -c Release
dotnet test tests/PlcDbSnapshot.Core.Tests
```
Visual Studio 2022에서는 `PlcDbSnapshot.sln`을 열고 `PlcDbSnapshot.App`을 시작 프로젝트로 빌드합니다.

## 구조

| 경로 | 내용 |
|---|---|
| `src/PlcDbSnapshot.App` | WinForms 화면, `PlcReader` (PLC 읽기 전용 래퍼) |
| `src/PlcDbSnapshot.Core` | 스냅샷 CSV 형식, 비교 로직 (PLC/Windows 의존 없음, 단위 테스트 있음) |
| `src/S7CommPlusDriver` | 드라이버 소스를 빌드하는 SDK 형식 프로젝트 |
| `third_party/S7CommPlusDriver` | 드라이버 소스 (LGPL-3.0), 변경점은 `THIRD_PARTY.md` |

## 주의

스냅샷 CSV에는 현장 PLC 주소와 데이터가 들어 있습니다. 이 저장소에는 올리지 마세요 (`.gitignore`에서 `*.csv` 제외됨).

## License

LGPL-3.0 (`LICENSE`). Third-party notices: `THIRD_PARTY.md`.
