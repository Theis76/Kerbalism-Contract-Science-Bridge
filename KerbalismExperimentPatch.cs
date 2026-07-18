KERBALISM_CONTRACT_SCIENCE_BRIDGE
{
    // Writes detailed matching decisions to KSP.log.
    debugLogging = false

    // Complete a matching CollectScience objective when a Kerbalism experiment
    // enters Running or Forced state and has a concrete science subject.
    completeOnExperimentStart = true

    // Complete a matching CollectScience objective when a Kerbalism file for
    // that subject has a positive transmitRate.
    completeOnTransmissionStart = true

    // The requested gameplay rule treats starting work as sufficient, so the
    // original Recover/Transmit/Ideal requirement is bypassed after a match.
    // Setting this to false disables the bridge's early-completion matching
    // entirely (both experiment-start and transmission-start evidence are
    // ignored) and leaves the objective to Contract Configurator's own
    // stock recovery/transmission logic, unchanged.
    ignoreRecoveryMethod = true

    // Polling period for transmission inspection. Experiment starts are event/
    // patch driven and do not depend on this interval.
    transmissionPollSeconds = 0.50

    // Evidence older than this is discarded. Contract parameters are normally
    // evaluated immediately, but a generous window protects against scene and
    // vessel update ordering differences.
    evidenceLifetimeSeconds = 30.0
}
