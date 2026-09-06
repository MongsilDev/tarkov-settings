# tarkov-settings

[![English](https://img.shields.io/badge/README-English-555555?style=flat-square)](README.en.md)
[![Hits](https://hits.sh/github.com/MongsilDev/tarkov-settings.svg?style=flat-square&label=hits&color=8c8c8c&labelColor=555555)](https://hits.sh)

![screenshot](./1.png)

## [->**최신 버전 다운로드**<-](https://github.com/MongsilDev/tarkov-settings/releases/latest)

Escape from Tarkov와 Arena가 포커스된 동안만 화면 색상을 바꾸는 프로그램.
[incheon-kim/tarkov-settings](https://github.com/incheon-kim/tarkov-settings) 포크에 기능과 수정을 더한 버전.

## 기능
- 밝기, 대비, 감마, Digital Vibrance를 게임 창이 포커스일 때만 적용. 알트탭 시 화면 번쩍임 없음
- Escape from Tarkov와 Arena 지원, Arena는 체크박스로 켜고 끔
- 핫키 3종: 감마 두 값 전환, 게임 볼륨 두 값 전환, 게임 즉시 종료. 게임 포커스 중에만 동작
- 게임 창이 있는 모니터 자동 추적
- 윈도우 시작 시 실행, 트레이 최소화 시작

## 사용법
1. exe 다운로드 후 우클릭 > 속성 > 차단 해제, 실행
2. 슬라이더로 색상 조절. Recommended는 추천값, Default는 윈도우 기본값, 라벨 더블클릭은 항목별 기본값
3. Arena에도 적용하려면 Apply to Arena 체크
4. Hotkeys에서 값 두 개와 키 지정. Key 칸을 클릭하고 키 입력, Esc 취소, Backspace 해제. 기본 키는 Gamma PageUp, Game volume PageDown. Kill game은 Ctrl/Alt/Shift 조합만 가능하고 지정할 때 한 번 확인
5. 필요하면 Start with Windows, Start minimized 체크

설정 파일: `%LOCALAPPDATA%\tarkov-settings\settings.json`, 앱이 닫힐 때 저장.

## 주의
1. 게임 창이 활성화될 때 화면이 한두 번 깜빡일 수 있음
2. BSG가 이 프로그램 사용을 제재할지는 알 수 없음
3. Borderless 모드에서만 동작
4. GPU: NVIDIA 전체 지원, AMD는 채도 제외, Intel 미지원
5. 서명 없는 빌드라 Defender나 SmartScreen 경고가 뜨면 허용 또는 제외 등록 필요
