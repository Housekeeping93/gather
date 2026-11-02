# Signal runtime in Gather 0.10.0 development builds

This local development package includes the following unmodified upstream distributions. Gather's Signal connection is unofficial.

| Component | Version | Upstream |
| --- | --- | --- |
| signal-cli JVM distribution | 0.14.7 | https://github.com/AsamK/signal-cli/releases/tag/v0.14.7 |
| Eclipse Temurin, Windows x64 JRE | 25.0.4.1+1-LTS | https://github.com/adoptium/temurin25-binaries/releases/tag/jdk-25.0.4.1%2B1 |
| libsignal-client, included by signal-cli | 0.99.1 | https://github.com/signalapp/libsignal/tree/v0.99.1 |

The downloaded signal-cli archive SHA-256 is `0E1EEFDF4A2109EDF7C899C9D1667167C54AC12C3EC824F27DB7C1DAC4FA7506`, matched against the asset digest returned by GitHub's official release API. The Temurin ZIP SHA-256 is `4C95451CEA98556DEF2C54F7782933F52A26D4A36BD85E1D59F0364464828B07`, matched against Eclipse Adoptium's official release API checksum.

The application uses its private Java runtime; it does not change the system Java installation. The complete file inventory is `runtimes/signal/runtime-manifest.json`. Temurin's notices and component license texts remain under `runtimes/signal/java/NOTICE` and `runtimes/signal/java/legal`. signal-cli's GPLv3 license and libsignal's AGPLv3 license are included beside this notice. Upstream dependency notices embedded in JARs are retained.

Corresponding source references: https://github.com/AsamK/signal-cli/tree/v0.14.7, https://github.com/signalapp/libsignal/tree/v0.99.1, and the Temurin release's source artifacts linked above. A complete redistribution/source-obligation review remains required before a public release; this package is for local development and phone-linking acceptance.
