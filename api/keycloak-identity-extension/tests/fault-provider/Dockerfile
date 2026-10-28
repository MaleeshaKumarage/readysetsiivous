# Synthetic fault-injection image ONLY; the ordinary Dockerfile excludes tests/.
ARG IDENTITY_BASE=cleaning-identity-rehearsal:ci
FROM maven:3.9.11-eclipse-temurin-21@sha256:6fdc855a6ed81d288ca7ca37ac6ff5e9308b612485c0801d70b25a858c83d237 AS test-build
WORKDIR /src
COPY tests/fault-provider/pom.xml ./
COPY tests/fault-provider/src ./src
RUN mvn -B -ntp package
FROM ${IDENTITY_BASE}
COPY --from=test-build --chown=keycloak:keycloak /src/target/synthetic-password-rollback-0.1.0-test.jar /opt/keycloak/providers/synthetic-password-rollback-test.jar
