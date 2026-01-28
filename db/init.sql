-- eSDSCom.Editor - minimal Postgres schema for local development/testing
--
-- Notes:
-- - The application code uses unquoted identifiers (e.g. SELECT * FROM SUBSTANCES),
--   so tables are created in lowercase (Postgres folds unquoted identifiers to lowercase).
-- - Use TEXT for long fields (e.g. substances.name) to avoid truncation.

CREATE TABLE IF NOT EXISTS users (
    id              uuid PRIMARY KEY,
    organizationid  uuid NOT NULL,
    email           text,
    name            text,
    role            integer NOT NULL DEFAULT 0,
    isactive        boolean NOT NULL DEFAULT true,
    createddate     timestamptz NOT NULL DEFAULT now(),
    updateddate     timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS organizations (
    id                              uuid PRIMARY KEY,
    organizationtype                text,
    name                            text,
    address                         text,
    informationfromexportingsystem  text,
    createddate                     timestamptz NOT NULL DEFAULT now(),
    updateddate                     timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS datasheets (
    id              uuid PRIMARY KEY,
    organizationid  uuid,
    userid          uuid,
    name            text,
    createddate     timestamptz NOT NULL DEFAULT now(),
    updateddate     timestamptz NOT NULL DEFAULT now(),
    status          integer NOT NULL DEFAULT 0,
    datasheetdoc    text,
    comments        text,
    regionsstring   text,
    materialtype    text,
    username        text
);

CREATE TABLE IF NOT EXISTS datasheetfeeds (
    id               uuid PRIMARY KEY,
    organizationid   uuid,
    userid           uuid,
    name             text,
    createddate      timestamptz NOT NULL DEFAULT now(),
    updateddate      timestamptz NOT NULL DEFAULT now(),
    datasheetfeeddoc text,
    comments         text,
    status           integer NOT NULL DEFAULT 0,
    username         text
);

CREATE TABLE IF NOT EXISTS datasheetfeeditems (
    datasheetfeedid  uuid NOT NULL,
    datasheetid      uuid NOT NULL,
    userid           uuid,
    createddate      timestamptz NOT NULL DEFAULT now(),
    updateddate      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (datasheetfeedid, datasheetid)
);

CREATE TABLE IF NOT EXISTS phrases (
    id        uuid PRIMARY KEY,
    struccode text NOT NULL,
    xpath     text,
    region    text,
    origcode  text,
    english   text,
    german    text,
    revdate   text,
    source    text,
    info      text,
    origin    text
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_phrases_struccode ON phrases (struccode);

CREATE TABLE IF NOT EXISTS substances (
    id                       uuid PRIMARY KEY,
    substanceid              text NOT NULL,
    name                     text,
    ecnumber                 text,
    casnumber                text,
    registrationstatus       text,
    registrationtype         text,
    submissiontype           text,
    tonnageband              text,
    tonnagebandmin           text,
    tonnagebandmax           text,
    lastupdated              text,
    factsheeturl             text,
    substanceinformationpage text
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_substances_substanceid ON substances (substanceid);

-- If you already have a DB created with a narrower column type (e.g. varchar(500)),
-- you can widen it safely:
--   ALTER TABLE substances ALTER COLUMN name TYPE text;
