# Home Assistant examples

These examples turn Windows Companion sensor reports into useful Home Assistant
entities and automations. They use supported Home Assistant configuration and do
not require a custom integration.

## Categories

| Category | Purpose | Installation |
| --- | --- | --- |
| [Templates](templates/) | Derive new entities from companion sensor state | Add the YAML to Home Assistant configuration or recreate it with the Template helper |
| [Automations](automations/) | React to companion sensor state | Each automation will identify its supported import or installation method |

Start with [Windows PC connectivity](templates/device-connectivity/) to mark
a PC disconnected when Home Assistant has not received a sensor report recently.

## Contributing an example

Put each example in the directory matching its Home Assistant artifact type.
Create one lowercase, hyphen-separated directory per example, such as
`device-connectivity/` or `mute-meeting-notification/`. Keep its instructions in
`README.md` and its configuration artifacts beside them.

Every example must include:

1. Its purpose and expected user-visible outcome.
2. Home Assistant and Windows Companion prerequisites.
3. Complete installation instructions.
4. Every value the user must customize.
5. Normal, unavailable-input, and recovery behavior.
6. Relevant timing limitations.
7. Complete removal instructions.

Use obvious placeholders such as `Replace with your device name`. Never
include Home Assistant URLs, webhook IDs, device identifiers, or real sensor
values.

Only describe an example as importable when Home Assistant provides an import
flow for that exact artifact. When available, include both the canonical source
URL and a My Home Assistant import link.
