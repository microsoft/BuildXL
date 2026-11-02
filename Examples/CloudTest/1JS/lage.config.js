module.exports = {
  pipeline: {
    build: ["^build"],
    lint: [],
    "test:e2e": ["build"],
    "test:component": ["build"],
  },
};
