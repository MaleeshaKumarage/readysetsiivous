# Development rehearsal only; production promotion requires the separate rollout gate.
FROM maven:3.9.11-eclipse-temurin-21@sha256:6fdc855a6ed81d288ca7ca37ac6ff5e9308b612485c0801d70b25a858c83d237 AS build
WORKDIR /src
COPY pom.xml ./
COPY src ./src
RUN mvn -B -ntp package

FROM quay.io/keycloak/keycloak@sha256:a78d6b79572168cb25886b80a8b5b8d4ed4390743e3eecb810538683e209dd6b
COPY --from=build --chown=keycloak:keycloak /src/target/cleaning-identity-extension-0.1.0-dev.jar /opt/keycloak/providers/cleaning-identity-extension.jar
