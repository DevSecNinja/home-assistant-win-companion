# Automations

This directory is reserved for Home Assistant automations built around Windows
Companion sensors.

Each automation will document:

- required companion sensors and Home Assistant version;
- values that must be customized;
- expected actions, timing, and recovery behavior;
- whether it is a blueprint, directly importable artifact, or manual YAML;
- its canonical source URL and My Home Assistant import link when supported;
- how to remove the automation.

Automation blueprints should include their source URL and an import link.
Ordinary automation YAML must not be described as one-click importable unless
Home Assistant adds a supported import flow for it.
