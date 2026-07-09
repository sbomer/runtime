config({
    resolvers: [
        {
            kind: "DScript",
            modules: [f`restore/module.config.dsc`],
        },
    ],
    mounts: [
        {
            name: a`UserProfile`,
            path: p`/home/sven`,
            isReadable: true,
            isWritable: true,
            trackSourceFileChanges: false,
        },
        {
            name: a`Artifacts`,
            path: p`artifacts`,
            isReadable: true,
            isWritable: true,
            isScrubbable: true,
            trackSourceFileChanges: true,
        },
    ],
});
