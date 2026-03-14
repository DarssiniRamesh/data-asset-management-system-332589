# Flyway migrator container (planned in CodeWiki).
# This image is intended to be run as a pre-start job/init step to apply migrations before the API starts.

FROM flyway/flyway:10

# Copy migrations into the default Flyway location.
COPY ./db/migrations /flyway/sql

# Optional entrypoint wrapper to ensure consistent command/args.
COPY ./flyway-migrator/entrypoint.sh /flyway/entrypoint.sh
RUN chmod +x /flyway/entrypoint.sh

ENTRYPOINT ["/flyway/entrypoint.sh"]
